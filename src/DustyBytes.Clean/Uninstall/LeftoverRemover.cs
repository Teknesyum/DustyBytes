using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public sealed record RemovalItem(string Id, LeftoverKind Kind, string Target, bool Ok, string Message, long Bytes = 0);

public sealed record RemovalReport(List<RemovalItem> Items, string? RegBackupFile, bool DryRun)
{
    public int Removed => Items.Count(i => i.Ok);
    public int Failed => Items.Count(i => !i.Ok);
}

public sealed class LeftoverRemover
{
    readonly IRegistryView _reg;
    readonly LeftoverScanner _scanner;
    readonly Func<string, Task<bool>> _quarantine;
    readonly ISystemActions _actions;
    readonly string _backupDir;

    public LeftoverRemover(IRegistryView registry, LeftoverScanner scanner, Func<string, Task<bool>> quarantine, ISystemActions? actions = null, string? backupDir = null)
    {
        _reg = registry;
        _scanner = scanner;
        _quarantine = quarantine;
        _actions = actions ?? SystemActions.Instance;
        _backupDir = backupDir ?? Path.Combine(Paths.AppData, "registry-backup");
    }

    public List<string> Log { get; } = [];

    public async Task<RemovalReport> Remove(LeftoverSnapshot snapshot, IReadOnlyCollection<string> approvedIds, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var approved = new HashSet<string>(approvedIds, StringComparer.OrdinalIgnoreCase);
        var targets = snapshot.Candidates.Where(c => approved.Contains(c.Id)).ToList();
        var items = new List<RemovalItem>();
        foreach (var id in approved.Where(a => !snapshot.Candidates.Any(c => c.Id.Equals(a, StringComparison.OrdinalIgnoreCase))))
            items.Add(new RemovalItem(id, LeftoverKind.File, id, false, "Anlık görüntüde böyle bir aday yok; dokunulmadı"));

        var dry = DryRun.Enabled;
        string? backupFile = null;
        var regItems = targets.Where(NeedsRegistryBackup).SelectMany(BackupItems).ToList();
        if (!dry && regItems.Count > 0)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeName = string.Concat(snapshot.Program.DisplayName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            backupFile = Path.Combine(_backupDir, $"{stamp}-{safeName}-{snapshot.Id[..8]}.reg");
            try
            {
                RegistryExport.ToRegFile(_reg, regItems, backupFile);
                Log.Add($"Kayıt defteri yedeği: {backupFile}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                backupFile = null;
                Log.Add($"Kayıt defteri yedeği yazılamadı: {e.Message}");
            }
        }

        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var c = targets[i];
            progress?.Report(new ScanProgress("Kalıntı kaldırılıyor", 100.0 * i / Math.Max(1, targets.Count), c.Target));

            if (!_scanner.Exists(c))
            {
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, true, "Zaten yok"));
                continue;
            }
            if (c.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut && _scanner.GateCandidate(c, snapshot.Program) is { } reason)
            {
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, false, reason));
                continue;
            }
            if (RegistryGate.Check(c) is { } blocked)
            {
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, false, "Korumalı kayıt alanı: " + blocked));
                continue;
            }
            if (c.ExpectValue is { } expect && c.Key is { } ek && c.ValueName is { } vn
                && !string.Equals(_reg.GetString(ek, vn), expect, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, false, "Değer artık programı göstermiyor; dokunulmadı"));
                continue;
            }
            if (dry)
            {
                Log.Add($"Prova kipi: {c.Kind} {c.Target}");
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, true, "Prova kipi: yalnız günlüğe yazıldı", c.Bytes));
                continue;
            }
            if (NeedsRegistryBackup(c) && backupFile is null)
            {
                items.Add(new RemovalItem(c.Id, c.Kind, c.Target, false, "Kayıt defteri yedeği alınamadı; silinmedi"));
                continue;
            }
            items.Add(await RemoveOne(c).ConfigureAwait(false));
        }
        if (!dry && backupFile is not null)
        {
            var removed = items.Where(i => i.Ok).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            items.AddRange(RemoveEmptyPublisherKeys(targets.Where(t => removed.Contains(t.Id)), backupFile));
        }
        progress?.Report(new ScanProgress("Kalıntı kaldırılıyor", 100));
        return new RemovalReport(items, backupFile, dry);
    }

    bool IsEmptyPublisherKey(RegKeyRef key)
    {
        var parts = RegistryGate.Logical(key).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)
            && RegistryGate.KeyBlock(key) is null && !NameMatcher.IsSharedPublisher(key.Name)
            && _reg.KeyExists(key) && _reg.GetSubKeyNames(key).Count == 0 && _reg.GetValueNames(key).Count == 0;
    }

    List<RemovalItem> RemoveEmptyPublisherKeys(IEnumerable<LeftoverCandidate> removed, string backupFile)
    {
        var parents = removed.Where(c => c.Kind == LeftoverKind.RegistryKey && c.Key is not null)
            .Select(c => c.Key!.Parent()).OfType<RegKeyRef>()
            .DistinctBy(k => k.Identity)
            .Where(IsEmptyPublisherKey)
            .ToList();
        var result = new List<RemovalItem>();
        if (parents.Count == 0)
            return result;
        var file = Path.ChangeExtension(backupFile, null) + "-parents.reg";
        try
        {
            RegistryExport.ToRegFile(_reg, parents, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Add($"Boş yayıncı anahtarı yedeklenemedi, dokunulmadı: {e.Message}");
            return result;
        }
        foreach (var k in parents)
        {
            var ok = _reg.DeleteKeyTree(k);
            result.Add(new RemovalItem("parent:" + k.Identity, LeftoverKind.RegistryKey, k.Display, ok,
                ok ? "Boş kalan yayıncı anahtarı silindi (yedek .reg dosyasında)" : "Boş yayıncı anahtarı silinemedi"));
        }
        return result;
    }

    static bool NeedsRegistryBackup(LeftoverCandidate c) => c.Key is not null && c.Kind is LeftoverKind.RegistryKey or LeftoverKind.RegistryValue
        or LeftoverKind.Service or LeftoverKind.StartupEntry or LeftoverKind.FileAssociation or LeftoverKind.FirewallRule;

    static IEnumerable<RegExportItem> BackupItems(LeftoverCandidate c) => c.Kind switch
    {
        LeftoverKind.RegistryValue or LeftoverKind.StartupEntry or LeftoverKind.FirewallRule => [new RegExportItem(c.Key!, c.ValueName)],
        _ => [new RegExportItem(c.Key!)],
    };

    async Task<RemovalItem> RemoveOne(LeftoverCandidate c)
    {
        try
        {
            switch (c.Kind)
            {
                case LeftoverKind.Folder:
                case LeftoverKind.File:
                case LeftoverKind.Shortcut:
                    var ok = await _quarantine(c.Target).ConfigureAwait(false);
                    return new RemovalItem(c.Id, c.Kind, c.Target, ok, ok ? "Karantinaya taşındı" : "Karantinaya taşınamadı", ok ? c.Bytes : 0);
                case LeftoverKind.RegistryKey:
                case LeftoverKind.FileAssociation:
                    return Result(c, _reg.DeleteKeyTree(c.Key!), "Anahtar silindi (yedek .reg dosyasında)", "Anahtar silinemedi");
                case LeftoverKind.RegistryValue:
                case LeftoverKind.StartupEntry:
                    return Result(c, _reg.DeleteValue(c.Key!, c.ValueName!), "Değer silindi (yedek .reg dosyasında)", "Değer silinemedi");
                case LeftoverKind.Service:
                    var s = _actions.DeleteService(c.Target);
                    return new RemovalItem(c.Id, c.Kind, c.Target, s.Ok, s.Message);
                case LeftoverKind.ScheduledTask:
                    if (c.Detail is { } xml && File.Exists(xml))
                    {
                        Directory.CreateDirectory(_backupDir);
                        File.Copy(xml, Path.Combine(_backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(xml)}.xml"), overwrite: true);
                    }
                    var t = _actions.DeleteScheduledTask(c.Target);
                    return new RemovalItem(c.Id, c.Kind, c.Target, t.Ok, t.Ok ? "Görev silindi (XML yedeği alındı)" : t.Message);
                case LeftoverKind.FirewallRule:
                    var f = _actions.DeleteFirewallRule(c.Key!, c.ValueName!, c.Target, c.Detail);
                    return new RemovalItem(c.Id, c.Kind, c.Target, f.Ok, f.Ok ? "Güvenlik duvarı kuralı silindi" : f.Message);
                default:
                    return new RemovalItem(c.Id, c.Kind, c.Target, false, "Desteklenmeyen tür");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new RemovalItem(c.Id, c.Kind, c.Target, false, e.Message);
        }
    }

    static RemovalItem Result(LeftoverCandidate c, bool ok, string yes, string no) =>
        new(c.Id, c.Kind, c.Target, ok, ok ? yes : no);
}
