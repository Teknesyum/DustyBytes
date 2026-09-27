using System.Text.Json;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.Uninstall;

public sealed class UninstallHandlers
{
    public const string SkipRestorePoint = "skip-restore-point";
    public const string ContinueWithoutRestorePoint = "continue-without-restore-point";
    public const string AutoClean = "auto-clean";
    public const string KeepSettings = "keep-settings";

    readonly ProtectedList _protection;
    readonly IRegistryView _reg;
    readonly IFileProbe _probe;
    readonly Func<string, Task<bool>>? _quarantine;
    readonly SnapshotStore _store;
    readonly ISystemActions? _actions;

    public UninstallHandlers(ProtectedList protection, Func<string, Task<bool>>? quarantine, IRegistryView? registry = null, IFileProbe? probe = null, SnapshotStore? store = null, ISystemActions? actions = null)
    {
        _protection = protection;
        _quarantine = quarantine;
        _reg = registry ?? WindowsRegistryView.Instance;
        _probe = probe ?? FileProbe.Instance;
        _store = store ?? new SnapshotStore();
        _actions = actions;
    }

    public Func<IReadOnlyList<InstalledProgram>>? ProgramProvider { get; init; }

    public Func<LeftoverScanner, Uninstaller>? UninstallerFactory { get; init; }

    public async Task<WorkerResponse> HandleUninstall(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (!request.UserApproved)
            return Fail(request, "Kullanıcı onayı yok; kaldırma reddedildi");
        if (string.IsNullOrWhiteSpace(request.Target))
            return Fail(request, "Kaldırılacak program belirtilmedi");

        var programs = ProgramProvider?.Invoke() ?? InstalledPrograms.Enumerate(_reg, _probe, new EnumerateOptions { MeasureSize = false });
        var program = programs.FirstOrDefault(p => p.Id.Equals(request.Target, StringComparison.Ordinal));
        if (program is null)
            return Fail(request, $"Program bulunamadı: {request.Target}");
        if (_protection.RuntimeReason(program.DisplayName) is { } runtime)
            return Fail(request, $"Korumalı: {runtime}");
        if (program.Source == ProgramSource.Msix && program.PackageFamilyName is { } family && _protection.IsProtectedAppx(family))
            return Fail(request, "Korumalı sistem paketi");
        if (!program.CanUninstall)
            return Fail(request, "Bu program için kaldırma komutu yok");

        var p = Map(request.Id, progress);
        var scanner = new LeftoverScanner(ScanContext.ForSystem(_protection, programs, _reg, _probe));
        var uninstaller = UninstallerFactory?.Invoke(scanner) ?? new Uninstaller(scanner);
        var items = new List<ItemResult>();

        RestorePointResult? restore = null;
        if (!request.Items.Contains(SkipRestorePoint))
        {
            restore = uninstaller.CreateRestorePoint($"DustyBytes: {program.DisplayName} kaldırılıyor", p);
            items.Add(new ItemResult("restore-point", restore.Ok, restore.Message));
            if (!restore.Ok && !request.Items.Contains(ContinueWithoutRestorePoint))
                return new WorkerResponse { Id = request.Id, Ok = false, Message = $"Uyarı: {restore.Message}. Devam etmek için onay gerekiyor", Items = items, DryRun = DryRun.Enabled };
        }

        var before = uninstaller.Snapshot(program, p, ct);
        items.Add(new ItemResult("snapshot", true, $"{before.Candidates.Count} aday kaydedildi"));

        var vendor = await uninstaller.RunVendorUninstaller(program, p, ct).ConfigureAwait(false);
        items.Add(new ItemResult("vendor", vendor.Ok, vendor.Message));
        if (restore is { Ok: true, Sequence: > 0 } r)
            _ = vendor.Ran ? RestorePoint.Complete(r.Sequence) : RestorePoint.Cancel(r.Sequence);

        var after = vendor.Ran && !DryRun.Enabled ? uninstaller.Diff(before, p) : before with { IsDiff = false };
        long freed = 0;
        if (request.Items.Contains(AutoClean))
        {
            var (cleaned, item) = await AutoCleanAsync(after, vendor, scanner, request.Items.Contains(KeepSettings), p, ct).ConfigureAwait(false);
            after = cleaned;
            items.Add(item);
            freed = cleaned.AutoRemoved.Where(i => i.Ok).Sum(i => i.Bytes);
        }
        _store.Save(after);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = vendor.Ok,
            Message = vendor.Message + (vendor.RebootRequired ? " (yeniden başlatma gerekiyor)" : ""),
            DryRun = DryRun.Enabled,
            Items = items,
            PendingBytes = freed,
            Payload = JsonSerializer.Serialize(after, UninstallJson.Default.LeftoverSnapshot),
        };
    }

    async Task<(LeftoverSnapshot Snapshot, ItemResult Item)> AutoCleanAsync(LeftoverSnapshot after, VendorResult vendor, LeftoverScanner scanner, bool keepSettings, IProgress<ScanProgress> p, CancellationToken ct)
    {
        if (DryRun.Enabled || !vendor.Ran)
            return (after, new ItemResult(AutoClean, true, "Kaldırıcı çalışmadı; kesin kalıntılar temizlenmedi"));
        if (!vendor.Ok || !after.IsDiff || after.ProgramStillInstalled)
            return (after, new ItemResult(AutoClean, false, "Program kaldırılmış görünmüyor; kalıntıya dokunulmadı"));
        var ids = after.Candidates.Where(c => c.AutoRemovable && !(keepSettings && c.IsSettings)).Select(c => c.Id).ToList();
        if (ids.Count == 0)
            return (after, new ItemResult(AutoClean, true, "Kesin kalıntı kalmadı"));
        var remover = new LeftoverRemover(_reg, scanner, _quarantine ?? (_ => Task.FromResult(false)), _actions);
        var report = await remover.Remove(after, ids, p, ct).ConfigureAwait(false);
        var done = report.Items.Where(i => i.Ok).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleaned = after with
        {
            Candidates = [.. after.Candidates.Where(c => !done.Contains(c.Id))],
            AutoRemoved = report.Items,
            RegBackupFile = report.RegBackupFile,
        };
        var message = $"{report.Removed} kesin kalıntı temizlendi" + (report.Failed > 0 ? $", {report.Failed} kalıntıya dokunulamadı" : "")
            + (report.RegBackupFile is { } f ? $"; kayıt yedeği {f}" : "");
        return (cleaned, new ItemResult(AutoClean, report.Failed == 0, message));
    }

    public async Task<WorkerResponse> HandleRemoveLeftovers(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (!request.UserApproved)
            return Fail(request, "Kullanıcı onayı yok; kalıntı silme reddedildi");
        var snapshot = _store.Load(request.UnitId);
        if (snapshot is null)
            return Fail(request, "Anlık görüntü bulunamadı");
        if (!snapshot.IsDiff)
            return Fail(request, "Kaldırıcı çalışmadan kalıntı silinmez; önce kaldırma tamamlanmalı");
        if (snapshot.ProgramStillInstalled)
            return Fail(request, "Program hâlâ kurulu; kalıntı silinmez");
        if (request.Items.Count == 0)
            return Fail(request, "Onaylı kalıntı yok");

        var programs = ProgramProvider?.Invoke() ?? InstalledPrograms.Enumerate(_reg, _probe, new EnumerateOptions { MeasureSize = false, IncludeMsix = false, DetectBySignature = false });
        var scanner = new LeftoverScanner(ScanContext.ForSystem(_protection, programs, _reg, _probe));
        var quarantine = _quarantine ?? (_ => Task.FromResult(false));
        var remover = new LeftoverRemover(_reg, scanner, quarantine, _actions);
        var report = await remover.Remove(snapshot, request.Items, Map(request.Id, progress), ct).ConfigureAwait(false);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = report.Failed == 0,
            Message = $"{report.Removed} kalıntı kaldırıldı, {report.Failed} başarısız" + (report.RegBackupFile is { } f ? $"; kayıt yedeği {f}" : ""),
            DryRun = report.DryRun,
            Items = report.Items.Select(i => new ItemResult(i.Target, i.Ok, i.Message, i.Bytes)).ToList(),
            PendingBytes = report.Items.Where(i => i.Ok).Sum(i => i.Bytes),
            Payload = JsonSerializer.Serialize(report, UninstallJson.Default.RemovalReport),
        };
    }

    static IProgress<ScanProgress> Map(string id, IProgress<WorkerProgress> target) =>
        new SyncProgress<ScanProgress>(s => target.Report(new WorkerProgress(id, s.Step, s.Percent, s.Line)));

    static WorkerResponse Fail(WorkerRequest r, string message) =>
        new() { Id = r.Id, Ok = false, Message = message, DryRun = DryRun.Enabled };

    sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
