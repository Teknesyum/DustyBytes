using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        StopDuplicates();
        IsFindingDuplicates = false;
        DuplicateStatus = "";
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
            FindDuplicates();
            var summary = $"{Format.Count(result.Units.Count)} birim, {Format.Bytes(result.Units.Sum(u => u.SizeBytes))} açılabilir";
            shell.Notify(refreshed ? "Değişiklikler işlendi: " + summary : "Tarama bitti: " + summary);
        }
        catch (OperationCanceledException)
        {
            shell.Notify("Tarama iptal edildi; önceki sonuç duruyor");
            FindDuplicates();
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

    public static TimeSpan DuplicateFlushDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    CancellationTokenSource? _duplicateCts;
    readonly List<Unit> _foundDuplicates = [];
    readonly Dictionary<string, string> _keepChoices = new(StringComparer.Ordinal);
    readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    bool _flushScheduled;

    public Task DuplicateSearch { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicateStatus))]
    [NotifyCanExecuteChangedFor(nameof(CancelDuplicatesCommand))]
    private bool _isFindingDuplicates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicateStatus))]
    private string _duplicateStatus = "";

    [ObservableProperty]
    private double _duplicatePercent;

    public bool HasDuplicateStatus => IsFindingDuplicates || DuplicateStatus.Length > 0;

    public void FindDuplicates()
    {
        StopDuplicates();
        if (Snapshot is not { } snapshot)
            return;
        _dismissed.Clear();
        var cts = _duplicateCts = new CancellationTokenSource();
        DuplicateSearch = RunDuplicatesAsync(snapshot, cts);
    }

    void StopDuplicates()
    {
        if (_duplicateCts is { } running)
        {
            running.Cancel();
            _duplicateCts = null;
        }
        _foundDuplicates.Clear();
    }

    [RelayCommand(CanExecute = nameof(IsFindingDuplicates))]
    private void CancelDuplicates()
    {
        StopDuplicates();
        IsFindingDuplicates = false;
        DuplicateStatus = "Kopya araması durduruldu";
    }

    async Task RunDuplicatesAsync(ScanSnapshot snapshot, CancellationTokenSource cts)
    {
        IsFindingDuplicates = true;
        DuplicatePercent = 0;
        DuplicateStatus = "Kopyalar aranıyor";
        var progress = new Progress<TaskStep>(step =>
        {
            if (cts != _duplicateCts)
                return;
            DuplicatePercent = Math.Clamp(step.Percent, 0, 100);
            var found = _foundDuplicates.Count + (Snapshot?.Units.Count(u => u.Kind == UnitKind.Duplicate) ?? 0);
            DuplicateStatus = found > 0 ? $"Kopyalar aranıyor · %{(int)DuplicatePercent} · {Format.Count(found)} grup" : $"Kopyalar aranıyor · %{(int)DuplicatePercent}";
        });
        var sink = new Progress<Unit>(unit =>
        {
            if (cts == _duplicateCts)
                Found(unit);
        });
        try
        {
            var units = await Task.Run(() => backend.FindDuplicatesAsync(snapshot, progress, ((IProgress<Unit>)sink).Report, cts.Token), cts.Token);
            await Task.Yield();
            if (cts != _duplicateCts)
                return;
            var ids = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            FlushDuplicates(ids, units);
            var bytes = Snapshot?.Units.Where(u => u.Kind == UnitKind.Duplicate).Sum(u => u.SizeBytes) ?? 0;
            DuplicateStatus = units.Count == 0 ? "Kopya bulunmadı" : $"{Format.Count(units.Count)} kopya grubu, {Format.Bytes(bytes)} açılabilir";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (cts == _duplicateCts)
                DuplicateStatus = "Kopya araması tamamlanamadı: " + e.Message;
        }
        finally
        {
            if (cts == _duplicateCts)
            {
                _duplicateCts = null;
                IsFindingDuplicates = false;
            }
            cts.Dispose();
        }
    }

    void Found(Unit unit)
    {
        _foundDuplicates.Add(unit);
        if (_flushScheduled)
            return;
        _flushScheduled = true;
        _ = FlushLaterAsync(_duplicateCts);
    }

    async Task FlushLaterAsync(CancellationTokenSource? owner)
    {
        await Task.Delay(DuplicateFlushDelay);
        _flushScheduled = false;
        if (owner is not null && owner == _duplicateCts)
            FlushDuplicates(null, null);
    }

    void FlushDuplicates(HashSet<string>? complete, IReadOnlyList<Unit>? all)
    {
        var incoming = all ?? [.. _foundDuplicates];
        _foundDuplicates.Clear();
        if (Snapshot is not { } snapshot)
            return;
        var gone = _dismissed;
        var fresh = incoming
            .Where(u => !gone.Contains(u.Id))
            .DistinctBy(u => u.Id)
            .Select(WithChoice)
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        var changed = false;
        var list = new List<Unit>(snapshot.Units.Count + fresh.Count);
        foreach (var unit in snapshot.Units)
        {
            if (unit.Kind == UnitKind.Duplicate && fresh.Remove(unit.Id, out var update))
            {
                list.Add(update);
                changed |= !ReferenceEquals(update, unit) && update != unit;
                continue;
            }
            if (unit.Kind == UnitKind.Duplicate && complete is not null && !complete.Contains(unit.Id))
            {
                changed = true;
                continue;
            }
            list.Add(unit);
        }
        if (fresh.Count > 0)
        {
            list.AddRange(fresh.Values);
            changed = true;
        }
        if (!changed)
            return;
        Snapshot = snapshot with { Units = list };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    Unit WithChoice(Unit unit) =>
        _keepChoices.TryGetValue(unit.Id, out var keep) ? DustyBytes.Units.DuplicateUnits.WithKeep(unit, keep) : unit;

    public void ChooseKeep(Unit unit)
    {
        if (unit.Kind != UnitKind.Duplicate || unit.Keep is null)
            return;
        _keepChoices[unit.Id] = unit.Keep;
        if (Snapshot is not { } snapshot)
            return;
        Snapshot = snapshot with { Units = [.. snapshot.Units.Select(u => u.Id == unit.Id ? unit : u)] };
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
        _dismissed.UnionWith(gone);
        if (_scan?.IsRunning == true)
            _removedDuringScan.UnionWith(gone);
        if (Draft is { } draft)
            SetDraft(draft);
        if (Snapshot is null)
            return;
        Snapshot = Snapshot with { Units = [.. Snapshot.Units.Where(u => !gone.Contains(u.Id))] };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateUnit(Unit unit)
    {
        if (Snapshot is null || !Snapshot.Units.Any(u => u.Id == unit.Id))
            return;
        Snapshot = Snapshot with { Units = [.. Snapshot.Units.Select(u => u.Id == unit.Id ? unit : u)] };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreUnits(IEnumerable<Unit> units)
    {
        _removedDuringScan.ExceptWith(units.Select(u => u.Id));
        _dismissed.ExceptWith(units.Select(u => u.Id));
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
