using CommunityToolkit.Mvvm.ComponentModel;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum ScanMode
{
    Refresh,
    Full,
    Fast,
}

public sealed partial class SessionState(IAppBackend backend) : ObservableObject
{
    TaskProgressViewModel? _scan;

    public TaskProgressViewModel Scan => _scan ?? throw new InvalidOperationException("Oturum başlatılmadı");

    public event EventHandler? SnapshotChanged;
    public event EventHandler? DraftChanged;

    readonly HashSet<string> _removedDuringScan = new(StringComparer.Ordinal);

    public ScanDraft? Draft { get; private set; }

    void SetDraft(ScanDraft? draft)
    {
        Draft = draft is null || _removedDuringScan.Count == 0
            ? draft
            : draft with { Units = [.. draft.Units.Where(u => !_removedDuringScan.Contains(u.Id))] };
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    private ScanSnapshot? _snapshot;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string? _selectedDrive;

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private string? _scanError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    private QuarantineSnapshot? _quarantine;

    public string PendingText => Quarantine is { } q ? $"{Format.Bytes(q.Entries.Sum(e => e.Size))}, {Format.Count(q.Entries.Count)} öğe" : "Okunuyor";

    [ObservableProperty]
    private LedgerData _ledger = new(0, 0);

    public bool HasSnapshot => Snapshot is not null;

    public void Attach(MainViewModel shell) => _scan ??= shell.NewProgress();

    public async Task StartAsync(MainViewModel shell)
    {
        Attach(shell);
        Ledger = backend.ReadLedger();
        _ = RefreshQuarantineAsync(shell);
        ScanSnapshot? cached = null;
        IsRestoring = true;
        try
        {
            cached = await Scan.RunAsync("Önceki tarama okunuyor", (p, ct) => backend.LoadCachedAsync(p, ct), cancellable: false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            shell.Fail("Önceki tarama okunamadı: " + e.Message);
        }
        finally
        {
            IsRestoring = false;
        }
        if (cached is not null)
            SetSnapshot(cached);
        await RunScanAsync(shell, ScanMode.Refresh);
    }

    public async Task RunScanAsync(MainViewModel shell, ScanMode mode)
    {
        Attach(shell);
        if (Scan.IsRunning)
            return;
        var cached = Snapshot;
        if (mode == ScanMode.Refresh && cached is null)
            mode = ScanMode.Full;
        IsRefreshing = Snapshot is not null;
        ScanError = null;
        _removedDuringScan.Clear();
        var drafts = Snapshot is null && mode == ScanMode.Full ? new Progress<ScanDraft>(d =>
        {
            if (Scan.IsRunning && Snapshot is null)
                SetDraft(d);
        }) : null;
        try
        {
            var title = mode switch
            {
                ScanMode.Fast => "Hızlı tarama yönetici izniyle yapılıyor",
                ScanMode.Refresh => "Son taramadan bu yana değişenler okunuyor",
                _ => "Sürücüler taranıyor",
            };
            var refreshed = false;
            var result = await Scan.RunAsync(title, async (p, ct) =>
            {
                if (mode == ScanMode.Refresh && await backend.RefreshAsync(cached!, p, ct) is { } fresh)
                {
                    refreshed = true;
                    return fresh;
                }
                if (mode == ScanMode.Fast)
                    return await backend.FastScanAsync(p, ct);
                if (backend.WorkerRunning && backend.FastScanAvailability().Enabled)
                {
                    try
                    {
                        return await backend.FastScanAsync(p, ct);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                return await backend.ScanAsync(p, ct, drafts);
            });
            if (_removedDuringScan.Count > 0)
                result = result with { Units = [.. result.Units.Where(u => !_removedDuringScan.Contains(u.Id))] };
            SetSnapshot(result);
            var summary = $"{Format.Count(result.Units.Count)} birim, {Format.Bytes(result.Units.Sum(u => u.SizeBytes))} açılabilir";
            shell.Notify(refreshed ? "Değişiklikler işlendi: " + summary : "Tarama bitti: " + summary);
        }
        catch (OperationCanceledException)
        {
            shell.Notify("Tarama iptal edildi; önceki sonuç duruyor");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Worker.WorkerStartException or TimeoutException)
        {
            ScanError = e.Message;
            shell.Fail("Tarama tamamlanamadı: " + e.Message);
        }
        finally
        {
            IsRefreshing = false;
            _removedDuringScan.Clear();
            if (Draft is not null)
                SetDraft(null);
        }
    }

    public void SetSnapshot(ScanSnapshot snapshot)
    {
        Snapshot = snapshot;
        if (SelectedDrive is { } drive && (snapshot.Results.Count < 2 || snapshot.For(drive) is null))
            SelectedDrive = null;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveUnits(IEnumerable<string> ids)
    {
        var gone = ids.ToHashSet(StringComparer.Ordinal);
        if (_scan?.IsRunning == true)
            _removedDuringScan.UnionWith(gone);
        if (Draft is { } draft)
            SetDraft(draft);
        if (Snapshot is null)
            return;
        Snapshot = Snapshot with { Units = [.. Snapshot.Units.Where(u => !gone.Contains(u.Id))] };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreUnits(IEnumerable<Unit> units)
    {
        _removedDuringScan.ExceptWith(units.Select(u => u.Id));
        if (Snapshot is null)
            return;
        var existing = Snapshot.Units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        Snapshot = Snapshot with { Units = [.. Snapshot.Units, .. units.Where(u => !existing.Contains(u.Id))] };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshQuarantineAsync(MainViewModel shell)
    {
        try
        {
            Quarantine = await backend.ReadQuarantineAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Worker.WorkerStartException)
        {
            shell.Fail("Karantina okunamadı: " + e.Message);
        }
    }

    public void AddFreed(long bytes, string? root = null) => Ledger = backend.AddFreed(bytes, root);
}
