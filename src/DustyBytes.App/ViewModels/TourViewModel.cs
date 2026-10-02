using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum TourStage
{
    Cleaning,
    Asking,
    Done,
}

public enum TourMode
{
    Full,
    Safe,
    Ask,
}

public sealed class TourSummary(string freedText, IReadOnlyList<string> lines, bool dryRun)
{
    public string FreedText { get; } = freedText;
    public IReadOnlyList<string> Lines { get; } = lines;
    public bool IsDryRun { get; } = dryRun;
    public string DryRunText => "Prova kipi: hiçbir dosya silinmedi, sayılar tahmindir";
}

public sealed partial class TourViewModel : ViewModelBase
{
    public static readonly TimeSpan LongUnused = TimeSpan.FromDays(90);
    public const string QuestionText = "Bunu silmek ister misiniz?";

    readonly MainViewModel _main;
    List<UnitCard> _items = [];
    int _index;
    long _cleaned;
    string? _cleanLine;
    long _quarantined;
    long _purged;
    int _quarantinedCount;
    int _purgedCount;
    int _kept;
    bool _dryRun;
    TourMode _mode;

    public TourViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
        Progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskProgressViewModel.IsRunning))
                Commands();
        };
        Purge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TwoStep.Target))
            {
                OnPropertyChanged(nameof(IsPurgeArmed));
                OnPropertyChanged(nameof(PurgeText));
            }
        };
        main.Ticked += Purge.Elapse;
    }

    public TaskProgressViewModel Progress { get; }
    public TwoStep Purge { get; } = new();
    public IReadOnlyList<UnitCard> Items => _items;
    public string Question => QuestionText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCleaning), nameof(IsAsking), nameof(IsDone))]
    private TourStage _stage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserData), nameof(CanPurgeCurrent))]
    private UnitCard? _current;

    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private TourSummary? _summary;

    public bool IsCleaning => Stage == TourStage.Cleaning;
    public bool IsAsking => Stage == TourStage.Asking;
    public bool IsDone => Stage == TourStage.Done;
    public bool HasUserData => Current?.HasUserData == true;
    public bool CanPurgeCurrent => Current?.NeverPurge != true;
    public bool IsPurgeArmed => Purge.IsArmed;
    public string PurgeText => IsPurgeArmed ? TwoStep.ArmedText : "Kalıcı sil";
    public string StepText => _items.Count == 0 ? "" : $"{Math.Min(_index + 1, _items.Count)} / {_items.Count}";
    public long FreedBytes => _cleaned + _purged;
    public long HeldBytes => _quarantined;
    public string FreedText => HeldBytes > 0
        ? $"Şimdi boşalan {Format.Bytes(FreedBytes)} · Karantinada {Format.Bytes(HeldBytes)}"
        : $"Şimdi boşalan {Format.Bytes(FreedBytes)}";
    public bool HasQuarantined => _quarantinedCount > 0;
    public string UserDataText => $"Burada kendi dosyalarınız olabilir. Karantinaya alırsanız {AppSettings.QuarantineDays.Days} gün içinde geri alırsınız.";

    public static bool Silent(Unit unit) => unit.Removal == RemovalMethod.DirectDelete && !unit.ContainsUserData;

    public static List<Unit> Pick(IEnumerable<Unit> units, DateTimeOffset now) =>
    [
        .. units.Where(u => u.Removal == RemovalMethod.Quarantine
                && u.Kind != UnitKind.Duplicate
                && u.SizeBytes >= OffersViewModel.SmallBytes
                && (u.Usage.LastUsed is not { } last || now - last >= LongUnused))
            .OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes),
    ];

    protected override void OnNavigatedFrom() => Purge.Reset();

    public async Task StartAsync(TourMode mode = TourMode.Full)
    {
        _main.Sessions.Begin("Tur", this);
        var units = _main.Session.Snapshot?.Units ?? [];
        var now = DateTimeOffset.Now;
        _mode = mode;
        _items = mode == TourMode.Safe ? [] : [.. Pick(units, now).Select(u => new UnitCard(u, now, () => { }))];
        var direct = units.Where(Silent).Select(u => new UnitCard(u, now, () => { })).ToList();
        _index = 0;
        _cleaned = _quarantined = _purged = 0;
        _quarantinedCount = _purgedCount = _kept = 0;
        _dryRun = false;
        _cleanLine = null;
        Summary = null;
        Current = null;
        Page = null;
        Purge.Reset();
        if (mode == TourMode.Ask)
        {
            Stage = TourStage.Asking;
            Show();
            return;
        }
        Stage = TourStage.Cleaning;
        Raise();

        var outcome = new CleanOutcome();
        try
        {
            await Progress.RunAsync("Güvenli artıklar temizleniyor", async (p, ct) =>
            {
                p.Report(new TaskStep("Önbellek ve geçici dosyalar bulunuyor", 5, null));
                var (options, tasks) = await CleanupViewModel.SafeDefaultsAsync(_main.Backend, ct);
                p.Report(new TaskStep("Önbellek ve geçici dosyalar siliniyor", 30, null));
                await CleanupViewModel.SendAsync(_main.Backend, options, tasks, outcome, p, ct);
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            _main.Notify("Temizlik iptal edildi");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Temizlik yapılamadı: " + e.Message);
        }
        _dryRun |= outcome.DryRun;
        _cleanLine = outcome.Summary;
        _main.Sessions.Cleaned(outcome.Tally);
        if (!outcome.DryRun)
        {
            _cleaned += outcome.Freed;
            if (outcome.Freed > 0)
                _main.Session.AddFreed(outcome.Freed);
        }
        foreach (var failure in outcome.Failures.Take(MainViewModel.toastMax))
            _main.Fail(failure);

        if (direct.Count > 0)
        {
            var removed = await _main.Offers.RemoveCoreAsync(direct, false, Progress, "Kendiliğinden yeniden oluşan dosyalar temizleniyor");
            if (removed.Error is { } error)
                _main.Fail("İşlem yapılamadı: " + error);
            _dryRun |= removed.DryRun;
            _cleaned += removed.DryRun ? 0 : removed.Freed;
            foreach (var failure in removed.Failures.Take(MainViewModel.toastMax))
                _main.Fail(failure);
        }
        Show();
    }

    void Show()
    {
        Purge.Reset();
        if (Stage != TourStage.Done && _index < _items.Count)
        {
            Current = _items[_index];
            Page = Current;
            Stage = TourStage.Asking;
        }
        else
        {
            Finish();
            return;
        }
        Raise();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(FreedBytes));
        OnPropertyChanged(nameof(HeldBytes));
        OnPropertyChanged(nameof(FreedText));
        OnPropertyChanged(nameof(HasQuarantined));
        Commands();
    }

    void Commands()
    {
        QuarantineCommand.NotifyCanExecuteChanged();
        PurgeOneCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        EndCommand.NotifyCanExecuteChanged();
    }

    bool CanAct() => IsAsking && Current is not null && !Progress.IsRunning;

    bool CanPurgeNow() => CanAct() && CanPurgeCurrent;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Quarantine()
    {
        if (Current is not { } card)
            return;
        Purge.Reset();
        var outcome = await _main.Offers.RemoveCoreAsync([card], false, Progress, TitleOf(card, false));
        Count(outcome, card, false);
        Next();
    }

    [RelayCommand(CanExecute = nameof(CanPurgeNow))]
    private async Task PurgeOne()
    {
        if (Current is not { } card || card.NeverPurge || !Purge.Press(card))
            return;
        var outcome = await _main.Offers.RemoveCoreAsync([card], true, Progress, TitleOf(card, true));
        Count(outcome, card, true);
        Next();
    }

    static string TitleOf(UnitCard card, bool purge) => OffersViewModel.TitleFor([card], purge);

    void Count(RemoveOutcome outcome, UnitCard card, bool purge)
    {
        if (outcome.Error is { } error)
        {
            _main.Fail("İşlem yapılamadı: " + error);
            return;
        }
        foreach (var failure in outcome.Failures)
            _main.Fail(failure);
        _dryRun |= outcome.DryRun;
        if (!outcome.DryRun && outcome.Moved.Count == 0)
            return;
        var bytes = outcome.DryRun ? card.Unit.SizeBytes : purge ? outcome.Freed : outcome.Pending;
        if (purge)
        {
            _purged += bytes;
            _purgedCount++;
        }
        else
        {
            _quarantined += bytes;
            _quarantinedCount++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Keep()
    {
        _kept++;
        Next();
    }

    void Next()
    {
        Olcum.Decided("tour");
        _index++;
        Show();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void End() => Finish();

    void Finish()
    {
        _main.Sessions.End();
        Purge.Reset();
        _kept += Math.Max(0, _items.Count - _index);
        _index = _items.Count;
        var lines = new List<string>();
        if (_mode != TourMode.Ask && (_cleaned > 0 || _quarantinedCount + _purgedCount == 0))
            lines.Add($"Önbellek ve geçici dosyalar: {Format.Bytes(_cleaned)}");
        if (_cleanLine is { } cleanLine)
            lines.Add(cleanLine);
        if (_quarantinedCount > 0)
            lines.Add($"Karantinaya alınan: {_quarantinedCount} öğe, {Format.Bytes(_quarantined)}. Bu yer karantina boşalınca açılır; {AppSettings.QuarantineDays.Days} gün içinde geri alabilirsiniz.");
        if (_purgedCount > 0)
            lines.Add($"Kalıcı silinen: {_purgedCount} öğe, {Format.Bytes(_purged)}");
        if (_kept > 0)
            lines.Add($"Yerinde kalan: {_kept} öğe");
        Summary = new TourSummary($"{Format.Bytes(FreedBytes)} boşaldı", lines, _dryRun);
        Current = null;
        Page = Summary;
        Stage = TourStage.Done;
        Raise();
    }

    [RelayCommand]
    private void Undo() => _main.GoTo(_main.Quarantine);

    [RelayCommand]
    private void Close()
    {
        if (!_main.Navigation.Pop())
            _main.GoTo(_main.Overview);
    }
}
