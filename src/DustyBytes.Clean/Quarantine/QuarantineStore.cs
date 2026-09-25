using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;

namespace DustyBytes.Clean.Quarantine;

public sealed record QuarantineOptions
{
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);
    public double WarnShare { get; init; } = 0.20;
    public Func<VolumeInfo, string>? RootResolver { get; init; }
    public bool ApplyAcl { get; init; } = true;
    public bool FallbackToRecycleBin { get; init; } = true;
}

public sealed record VolumeUsage(string Root, string VolumeId, int Count, long PendingBytes, long VolumeBytes, bool Warning);

public sealed class QuarantineStore
{
    const FileAttributes Restorable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
        FileAttributes.Archive | FileAttributes.NotContentIndexed | FileAttributes.Temporary;

    readonly SafetyGate _gate;
    readonly QuarantineOptions _options;
    readonly RecycleBin? _recycle;
    readonly HashSet<string> _roots = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    public QuarantineStore(SafetyGate gate, QuarantineOptions? options = null, RecycleBin? recycleBin = null)
    {
        _gate = gate;
        _options = options ?? new QuarantineOptions();
        _recycle = recycleBin ?? (_options.FallbackToRecycleBin ? new RecycleBin(gate) : null);
    }

    public static string DefaultRoot(VolumeInfo volume) =>
        Path.Combine(volume.Root, Paths.QuarantineDir, "quarantine");

    string RootFor(VolumeInfo volume) =>
        Paths.Normalize((_options.RootResolver ?? DefaultRoot)(volume));

    public IReadOnlyList<string> KnownRoots()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
            found.UnionWith(_roots);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                continue;
            try
            {
                if (!drive.IsReady || VolumeInfo.For(drive.RootDirectory.FullName) is not { } vol)
                    continue;
                var root = RootFor(vol);
                if (QuarantineManifest.ExistsIn(root))
                    found.Add(root);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return [.. found.Where(QuarantineManifest.ExistsIn)];
    }

    static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void EnsureRoot(string root)
    {
        var fresh = !Directory.Exists(root) || !QuarantineManifest.ExistsIn(root);
        Directory.CreateDirectory(root);
        if (fresh)
        {
            var parent = Path.GetDirectoryName(root);
            if (parent is not null && Path.GetFileName(parent).Equals(Paths.QuarantineDir, StringComparison.OrdinalIgnoreCase))
                File.SetAttributes(parent, File.GetAttributes(parent) | FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(root, File.GetAttributes(root) | FileAttributes.Hidden | FileAttributes.System);
            if (_options.ApplyAcl)
                Secure(root);
        }
        _ = new QuarantineManifest(root);
        lock (_lock)
            _roots.Add(root);
    }

    static void Secure(string root)
    {
        var elevated = IsElevated();
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        using (var id = WindowsIdentity.GetCurrent())
        {
            if (id.User is { } user && !user.Equals(system))
                sec.AddAccessRule(new FileSystemAccessRule(user, elevated ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        }
        if (elevated)
            sec.SetOwner(admins);
        new DirectoryInfo(root).SetAccessControl(sec);
    }

    static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);

    (QuarantineManifest Manifest, QuarantineEntry Entry)? Find(string id)
    {
        if (!ValidId(id))
            return null;
        foreach (var root in KnownRoots())
        {
            var manifest = new QuarantineManifest(root);
            if (manifest.Get(id) is { } entry)
                return (manifest, entry);
        }
        return null;
    }

    OpResult Fallback(string plain, bool includeUserData, string reason)
    {
        if (_recycle is null)
            return OpResult.Failed(plain, OpMethod.Quarantine, reason + "; Geri Dönüşüm Kutusu kapalı, işlem reddedildi");
        var result = _recycle.Send(plain, includeUserData);
        return result with { Message = $"{reason}. {result.Message}" };
    }

    public OpResult Quarantine(string path, string? unitId = null, bool includeUserData = false)
    {
        var verdict = _gate.Check(path, includeUserData);
        if (!verdict.Allowed)
            return OpResult.Denied(path, verdict, OpMethod.Quarantine);
        var plain = Paths.Normalize(path);
        var src = Paths.ToLong(plain);
        if (!FileTree.Exists(plain))
            return OpResult.NotFound(plain, OpMethod.Quarantine);

        var volume = VolumeInfo.For(plain);
        if (volume is null)
            return Fallback(plain, includeUserData, "Birim bilgisi okunamadı");
        if (!volume.SupportsQuarantine)
        {
            var why = volume.Remote ? "Ağ birimi" : volume.ReadOnly ? "Salt okunur birim" : $"{volume.FileSystem} izin listesi desteklemiyor";
            return Fallback(plain, includeUserData, $"{why}: karantina kurulamaz");
        }

        var root = RootFor(volume);
        if (Paths.IsUnder(plain, root) || Paths.IsUnder(root, plain))
            return OpResult.Denied(plain, Core.Protection.Verdict.Deny("Karantina kökünü kapsıyor", Core.Protection.Badge.System), OpMethod.Quarantine);

        var isDir = Directory.Exists(src);
        FileSystemInfo info = isDir ? new DirectoryInfo(src) : new FileInfo(src);
        var (bytes, count) = FileTree.Measure(plain);
        string? acl = null;
        try
        {
            acl = info is DirectoryInfo d
                ? d.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : ((FileInfo)info).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PrivilegeNotHeldException)
        {
        }

        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Quarantine, plain, $"{count} öğe, {bytes} bayt karantinaya taşınacaktı: {root}");
            return new OpResult { Path = plain, Status = OpStatus.DryRun, Method = OpMethod.Quarantine, Bytes = bytes, Message = "Prova: karantinaya taşınacaktı" };
        }

        try
        {
            EnsureRoot(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fallback(plain, includeUserData, $"Karantina kökü yazılamıyor ({e.Message})");
        }
        if (VolumeInfo.For(root) is not { } rootVolume || !rootVolume.Id.Equals(volume.Id, StringComparison.OrdinalIgnoreCase))
            return Fallback(plain, includeUserData, "Karantina kökü farklı birimde; birimler arası taşıma yapılmaz");

        var now = DateTime.UtcNow;
        var entry = new QuarantineEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            OriginalPath = plain,
            Root = root,
            IsDirectory = isDir,
            Size = bytes,
            Attributes = (int)info.Attributes,
            CreatedUtc = info.CreationTimeUtc,
            ModifiedUtc = info.LastWriteTimeUtc,
            AccessedUtc = info.LastAccessTimeUtc,
            Acl = acl,
            VolumeId = volume.Id,
            MovedUtc = now,
            ExpiresUtc = now + _options.Retention,
            UnitId = unitId,
            State = QuarantineState.Moving,
        };
        var manifest = new QuarantineManifest(root);
        manifest.Insert(entry);

        if (!Native.MoveFileEx(src, Paths.ToLong(entry.StoredPath), Native.MOVEFILE_WRITE_THROUGH))
        {
            var error = Marshal.GetLastPInvokeError();
            manifest.Remove(entry.Id);
            return error switch
            {
                Native.ERROR_SHARING_VIOLATION or Native.ERROR_LOCK_VIOLATION => new OpResult
                {
                    Path = plain,
                    Status = OpStatus.Locked,
                    Method = OpMethod.Quarantine,
                    Holders = isDir ? [] : [.. LockInfo.TryHolders([plain])],
                    Message = "Dosya başka bir süreç tarafından kullanılıyor",
                },
                Native.ERROR_ACCESS_DENIED when isDir => new OpResult
                {
                    Path = plain,
                    Status = OpStatus.Locked,
                    Method = OpMethod.Quarantine,
                    Message = "Klasör taşınamadı: içinde açık bir dosya ya da erişim engeli var",
                },
                Native.ERROR_NOT_SAME_DEVICE => OpResult.Failed(plain, OpMethod.Quarantine, "Birimler arası taşıma yapılmaz"),
                _ => OpResult.Failed(plain, OpMethod.Quarantine, new Win32Exception(error).Message),
            };
        }

        manifest.SetState(entry.Id, QuarantineState.Pending);
        return new OpResult
        {
            Path = plain,
            Status = OpStatus.Done,
            Method = OpMethod.Quarantine,
            Bytes = bytes,
            Id = entry.Id,
            Message = "Karantinaya alındı; bekleyen alan, geri alınabilir",
        };
    }

    public OpResult Restore(string id)
    {
        if (Find(id) is not ({ } manifest, { } entry))
            return OpResult.NotFound(id, OpMethod.Restore) with { Message = "Karantina kaydı bulunamadı", Id = id };
        var target = entry.OriginalPath;
        if (entry.State != QuarantineState.Pending)
            return OpResult.Failed(target, OpMethod.Restore, $"Öğe bekleyen durumda değil ({entry.State})") with { Id = id };
        if (!FileTree.Exists(entry.StoredPath))
            return OpResult.Failed(target, OpMethod.Restore, "Karantinadaki öğe kayıp") with { Id = id };
        if (FileTree.Exists(target))
            return new OpResult { Path = target, Status = OpStatus.Conflict, Method = OpMethod.Restore, Id = id, Message = "Orijinal yolda başka bir öğe var; üzerine yazılmadı" };

        var parent = Path.GetDirectoryName(target);
        if (parent is null)
            return OpResult.Failed(target, OpMethod.Restore, "Üst klasör çözülemedi") with { Id = id };
        var link = Core.Protection.ProtectedList.CheckFileSystem(parent, _gate.List.AttributeProvider);
        if (!link.Allowed)
            return OpResult.Denied(target, link, OpMethod.Restore) with { Id = id };

        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Restore, target, $"karantinadan geri yüklenecekti: {entry.Id}");
            return new OpResult { Path = target, Status = OpStatus.DryRun, Method = OpMethod.Restore, Id = id, Bytes = entry.Size, Message = "Prova: geri yüklenecekti" };
        }

        var recreated = false;
        if (!Directory.Exists(Paths.ToLong(parent)))
        {
            Directory.CreateDirectory(Paths.ToLong(parent));
            recreated = true;
        }

        if (!Native.MoveFileEx(Paths.ToLong(entry.StoredPath), Paths.ToLong(target), Native.MOVEFILE_WRITE_THROUGH))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is Native.ERROR_ALREADY_EXISTS or Native.ERROR_FILE_EXISTS)
                return new OpResult { Path = target, Status = OpStatus.Conflict, Method = OpMethod.Restore, Id = id, Message = "Orijinal yolda başka bir öğe var; üzerine yazılmadı" };
            return OpResult.Failed(target, OpMethod.Restore, new Win32Exception(error).Message) with { Id = id };
        }

        var restored = Paths.ToLong(target);
        FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(restored) : new FileInfo(restored);
        try
        {
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                info.Attributes &= ~FileAttributes.ReadOnly;
            info.CreationTimeUtc = entry.CreatedUtc;
            info.LastWriteTimeUtc = entry.ModifiedUtc;
            info.LastAccessTimeUtc = entry.AccessedUtc;
            var attrs = (FileAttributes)entry.Attributes & Restorable;
            info.Attributes = attrs == 0 ? (entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal) : attrs;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        manifest.SetState(id, QuarantineState.Restored);
        return new OpResult
        {
            Path = target,
            Status = OpStatus.Done,
            Method = OpMethod.Restore,
            Id = id,
            Bytes = entry.Size,
            Message = recreated ? "Geri yüklendi; üst klasör yeniden kuruldu" : "Geri yüklendi",
        };
    }

    public OpResult Purge(string id)
    {
        if (Find(id) is not ({ } manifest, { } entry))
            return OpResult.NotFound(id, OpMethod.Purge) with { Message = "Karantina kaydı bulunamadı", Id = id };
        return PurgeEntry(manifest, entry);
    }

    OpResult PurgeEntry(QuarantineManifest manifest, QuarantineEntry entry)
    {
        if (entry.State != QuarantineState.Pending)
            return OpResult.Failed(entry.OriginalPath, OpMethod.Purge, $"Öğe bekleyen durumda değil ({entry.State})") with { Id = entry.Id };
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Purge, entry.OriginalPath, $"karantinadan kalıcı silinecekti: {entry.Id}, {entry.Size} bayt");
            return new OpResult { Path = entry.OriginalPath, Status = OpStatus.DryRun, Method = OpMethod.Purge, Id = entry.Id, Bytes = entry.Size, Message = "Prova: kalıcı silinecekti" };
        }
        var report = FileTree.Delete(entry.StoredPath);
        if (!report.Complete)
            return new OpResult
            {
                Path = entry.OriginalPath,
                Status = report.Locked.Count > 0 ? OpStatus.Locked : OpStatus.Failed,
                Method = OpMethod.Purge,
                Id = entry.Id,
                Bytes = report.RemovedBytes,
                Skipped = [.. report.Locked, .. report.Failed, .. report.Remaining],
                Message = "Karantina öğesi tamamen silinemedi",
            };
        manifest.SetState(entry.Id, QuarantineState.Purged);
        return new OpResult { Path = entry.OriginalPath, Status = OpStatus.Done, Method = OpMethod.Purge, Id = entry.Id, Bytes = entry.Size, Message = "Kalıcı silindi, yer açıldı" };
    }

    public IReadOnlyList<OpResult> PurgeExpired(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var results = new List<OpResult>();
        foreach (var root in KnownRoots())
        {
            var manifest = new QuarantineManifest(root);
            Recover(manifest);
            foreach (var entry in manifest.Expired(now))
                results.Add(PurgeEntry(manifest, entry));
        }
        return results;
    }

    static void Recover(QuarantineManifest manifest)
    {
        foreach (var entry in manifest.All(includeClosed: false).Where(e => e.State == QuarantineState.Moving))
        {
            if (FileTree.Exists(entry.StoredPath))
                manifest.SetState(entry.Id, QuarantineState.Pending);
            else
                manifest.Remove(entry.Id);
        }
    }

    public IReadOnlyList<QuarantineEntry> List(bool includeClosed = false) =>
        [.. KnownRoots().SelectMany(r => new QuarantineManifest(r).All(includeClosed)).OrderByDescending(e => e.MovedUtc)];

    public IReadOnlyList<VolumeUsage> Usage()
    {
        var result = new List<VolumeUsage>();
        foreach (var root in KnownRoots())
        {
            var pending = new QuarantineManifest(root).All(includeClosed: false).Where(e => e.State == QuarantineState.Pending).ToList();
            var bytes = pending.Sum(e => e.Size);
            var volume = VolumeInfo.For(root);
            var total = volume?.TotalBytes ?? 0;
            result.Add(new VolumeUsage(root, volume?.Id ?? "", pending.Count, bytes, total, total > 0 && bytes > total * _options.WarnShare));
        }
        return result;
    }

    public bool SetExpiry(string id, DateTime expiresUtc)
    {
        if (Find(id) is not ({ } manifest, _))
            return false;
        manifest.SetExpiry(id, expiresUtc);
        return true;
    }
}
