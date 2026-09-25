using CommunityToolkit.Mvvm.ComponentModel;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class SessionState(IAppBackend backend) : ObservableObject
{
    TaskProgressViewModel? _scan;

    public TaskProgressViewModel Scan => _scan ?? throw new InvalidOperationException("Oturum başlatılmadı");

    public event EventHandler? SnapshotChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    private ScanSnapshot? _snapshot;

    [ObservableProperty]
    private bool _isRefreshing;

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
        await RunScanAsync(shell, fast: false);
    }

    public async Task RunScanAsync(MainViewModel shell, bool fast)
    {
        Attach(shell);
        if (Scan.IsRunning)
            return;
        IsRefreshing = Snapshot is not null;
        ScanError = null;
        try
        {
            var title = fast ? "Hızlı tarama yönetici izniyle yapılıyor" : $"{backend.ScanRoot} sürücüsü taranıyor";
            var result = await Scan.RunAsync(title, (p, ct) => fast ? backend.FastScanAsync(p, ct) : backend.ScanAsync(p, ct));
            SetSnapshot(result);
            shell.Notify($"Tarama bitti: {Format.Count(result.Units.Count)} birim, {Format.Bytes(result.Units.Sum(u => u.SizeBytes))} açılabilir");
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
        }
    }

    public void SetSnapshot(ScanSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveUnits(IEnumerable<string> ids)
    {
        if (Snapshot is null)
            return;
        var gone = ids.ToHashSet(StringComparer.Ordinal);
        Snapshot = Snapshot with { Units = [.. Snapshot.Units.Where(u => !gone.Contains(u.Id))] };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreUnits(IEnumerable<Unit> units)
    {
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

    public void AddFreed(long bytes) => Ledger = backend.AddFreed(bytes);
}
