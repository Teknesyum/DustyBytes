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
    Target,
}

public enum TourKey
{
    Enter,
    Escape,
    Left,
    Right,
}

public enum ClusterChoice
{
    All,
    Select,
    Keep,
}

public sealed class TourSummary(string freedText, IReadOnlyList<string> lines, bool dryRun, string? driveText = null, string? estimateText = null, IReadOnlyList<FailureNote>? problems = null)
{
    public string FreedText { get; } = freedText;
    public IReadOnlyList<string> Lines { get; } = lines;
    public bool IsDryRun { get; } = dryRun;
    public string DryRunText => "Prova kipi: hiçbir dosya silinmedi, sayılar tahmindir";
    public string DriveText { get; } = driveText ?? "";
    public bool HasDriveText => DriveText.Length > 0;
    public string EstimateText { get; } = estimateText ?? "";
    public bool HasEstimate => EstimateText.Length > 0;
    public IReadOnlyList<FailureNote> Problems { get; } = problems ?? [];
    public IReadOnlyList<string> ProblemLines => [.. Problems.Select(p => p.Text)];
    public bool HasProblems => Problems.Count > 0;
    public string ProblemsTitle => $"Silinemeyenler ({Format.Count(Problems.Count)})";
}

public sealed partial class ClusterItem(TourCluster owner, UnitCard card) : ObservableObject
{
    public UnitCard Card { get; } = card;
    public string Name => Card.Name;
    public string SizeText => Card.SizeText;
    public string UsageText => Card.UsageText;
    public bool HasUserData => Card.HasUserData;
    public string UserDataHint => "Kendi dosyalarınız olabilir";

    [ObservableProperty]
    private bool _isIncluded = true;

    partial void OnIsIncludedChanged(bool value) => owner.Changed();
}

public sealed partial class TourCluster : ObservableObject
{
    readonly Action _changed;

    public TourCluster(UnitGroup group, DateTimeOffset now, Action changed)
    {
        Group = group;
        _changed = changed;
        Items = [.. group.Units.Select(u => new ClusterItem(this, new UnitCard(u, now, () => { })))];
    }

    public UnitGroup Group { get; }
    public IReadOnlyList<ClusterItem> Items { get; }
    public string KindLabel => KindText.Label(Group.Kind);
    public string Title => Group.Title;
    public string SizeText => Format.Bytes(Group.Bytes);
    public string Effect => KindText.Effect(Group.Units[0] with { Effect = "" });
    public string PreviewText => Items.Count <= 3
        ? string.Join(", ", Items.Select(i => i.Name))
        : $"{string.Join(", ", Items.Take(3).Select(i => i.Name))} ve {Format.Count(Items.Count - 3)} öğe daha";
    public int UserDataCount => Items.Count(i => i.HasUserData);
    public bool HasUserData => UserDataCount > 0;
    public string UserDataText => (UserDataCount == Items.Count ? "Bunlarda kendi dosyalarınız olabilir" : $"{Format.Count(UserDataCount)} tanesinde kendi dosyalarınız olabilir")
        + $". Karantinaya alırsanız {AppSettings.QuarantineDays.Days} gün içinde geri alırsınız; emin değilseniz seçerek gidin.";
    public IReadOnlyList<ClusterItem> Chosen => [.. Items.Where(i => i.IsIncluded)];
    public int ChosenCount => Items.Count(i => i.IsIncluded);
    public string ChosenText => $"{Format.Count(ChosenCount)} / {Format.Count(Items.Count)} seçili, {Format.Bytes(Chosen.Sum(i => Math.Max(0, i.Card.Unit.SizeBytes)))}";

    public ClusterChoice Recommended => IsExpanded
        ? ChosenCount > 0 ? ClusterChoice.All : ClusterChoice.Keep
        : HasUserData ? ClusterChoice.Select : ClusterChoice.All;

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value)
            foreach (var item in Items)
                item.IsIncluded = true;
        Changed();
    }

    internal void Changed()
    {
        OnPropertyChanged(nameof(ChosenCount));
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(ChosenText));
        OnPropertyChanged(nameof(Recommended));
        _changed();
    }
}

public sealed partial class TourViewModel : ViewModelBase
{
    public static readonly TimeSpan LongUnused = TimeSpan.FromDays(90);
    public const string QuestionText = "Bunu silmek ister misiniz?";
    public const string ClusterQuestionText = "Bu kümeyi ne yapalım?";

    readonly MainViewModel _main;
    List<object> _steps = [];
    readonly HashSet<object> _decided = [];
    int _index;
    long _cleaned;
    string? _cleanLine;
    long _quarantined;
    long _purged;
    int _quarantinedCount;
    int _purgedCount;
    int _kept;
    int _skipped;
    bool _dryRun;
    bool _ranSafe;
    SpaceMeter? _meter;
    readonly List<FailureNote> _problems = [];
    TourMode _mode;
    TargetPlan? _plan;

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
    public IReadOnlyList<object> Steps => _steps;
    public string Question => QuestionText;
    public string ClusterQuestion => ClusterQuestionText;
    public string UncertainText => "Emin değiliz · tek tek soruyoruz";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCleaning), nameof(IsAsking), nameof(IsDone))]
    private TourStage _stage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserData), nameof(CanPurgeCurrent), nameof(IsCardStep), nameof(IsUncertain))]
    private UnitCard? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClusterStep))]
    private TourCluster? _cluster;

    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private TourSummary? _summary;

    public bool IsCleaning => Stage == TourStage.Cleaning;
    public bool IsAsking => Stage == TourStage.Asking;
    public bool IsDone => Stage == TourStage.Done;
    public bool IsClusterStep => Cluster is not null;
    public bool IsCardStep => Current is not null;
    public bool IsUncertain => Current is { } card && UnitClusters.Uncertain(card.Unit);
    public bool HasUserData => Current?.HasUserData == true;
    public bool CanPurgeCurrent => Current is { NeverPurge: false };
    public bool IsPurgeArmed => Purge.IsArmed;
    public string PurgeText => IsPurgeArmed ? TwoStep.ArmedText : "Kalıcı sil";
    public string Title => _mode switch
    {
        TourMode.Target => "Hedefli temizlik",
        TourMode.Ask => "Küme küme karar",
        _ => "Otomatik temizlik",
    };
    public string StepText => _steps.Count == 0 ? "" : $"{Math.Min(_index + 1, _steps.Count)} / {_steps.Count}";
    public long FreedBytes => _cleaned + _purged;
    public long HeldBytes => _quarantined;
    public string FreedText => HeldBytes > 0
        ? $"Şimdi boşalan {Format.Bytes(FreedBytes)} · Karantinada {Format.Bytes(HeldBytes)}"
        : $"Şimdi boşalan {Format.Bytes(FreedBytes)}";
    public bool HasQuarantined => _quarantinedCount > 0;
    public string UserDataText => $"Burada kendi dosyalarınız olabilir. Karantinaya alırsanız {AppSettings.QuarantineDays.Days} gün içinde geri alırsınız.";

    public ClusterChoice Recommended => Cluster is { } c ? c.Recommended
        : Current is { } card && (UnitClusters.Uncertain(card.Unit) || card.HasUserData) ? ClusterChoice.Keep
        : ClusterChoice.All;
    public bool AllIsRecommended => IsAsking && (IsClusterStep || IsCardStep) && Recommended == ClusterChoice.All;
    public bool SelectIsRecommended => IsAsking && IsClusterStep && Recommended == ClusterChoice.Select;
    public bool KeepIsRecommended => IsAsking && (IsClusterStep || IsCardStep) && Recommended == ClusterChoice.Keep;
    public string AllText => Cluster is { IsExpanded: true } c ? $"Seçilenleri karantinaya al ({Format.Count(c.ChosenCount)})" : "Hepsini karantinaya al";
    public string SelectText => Cluster is { IsExpanded: true } ? "Seçimi kapat" : "Seçerek";
    public string KeysText => $"Enter: {RecommendedName} · Esc: kalsın · ← →: önceki ya da sonraki {(IsClusterStep ? "küme" : "öğe")}";

    string RecommendedName => Recommended switch
    {
        ClusterChoice.Select => "seçerek",
        ClusterChoice.Keep => "kalsın",
        _ => IsClusterStep ? AllText.ToLowerInvariant() : "karantinaya al",
    };

    public static bool Silent(Unit unit) => unit.Removal == RemovalMethod.DirectDelete && !unit.ContainsUserData;

    public static List<Unit> Pick(IEnumerable<Unit> units, DateTimeOffset now) =>
    [
        .. units.Where(u => u.Removal == RemovalMethod.Quarantine
                && u.Kind != UnitKind.Duplicate
                && u.SizeBytes >= OffersViewModel.SmallBytes
                && (u.Usage.LastUsed is not { } last || now - last >= LongUnused))
            .OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes),
    ];

    public static List<object> Build(IEnumerable<Unit> units, DateTimeOffset now, Action changed)
    {
        var (groups, unsure) = UnitClusters.Build(units, now);
        return [.. groups.Select(g => (object)new TourCluster(g, now, changed)), .. unsure.Select(u => (object)new UnitCard(u, now, () => { }))];
    }

    protected override void OnNavigatedFrom() => Purge.Reset();

    public Task StartAsync(TourMode mode = TourMode.Full) => RunAsync(mode, null, false);

    public Task StartPlanAsync(TargetPlan plan, bool purge) => RunAsync(TourMode.Target, plan, purge);

    async Task RunAsync(TourMode mode, TargetPlan? plan, bool purge)
    {
        _main.Sessions.Begin(mode == TourMode.Target ? "Hedef" : "Tur", this);
        _meter = SpaceMeter.Start(_main.Backend);
        _problems.Clear();
        var units = _main.Session.Snapshot?.Units ?? [];
        var now = DateTimeOffset.Now;
        _mode = mode;
        _plan = plan;
        _steps = mode is TourMode.Full or TourMode.Ask ? Build(Pick(units, now), now, OnClusterChanged) : [];
        _decided.Clear();
        var skips = _main.Session.SafeSkips.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var silent = units.Where(Silent).ToList();
        var direct = silent.Where(u => !skips.Contains(SafeBreakdown.UnitKey(u.Id))).Select(u => new UnitCard(u, now, () => { })).ToList();
        _skipped = silent.Count - direct.Count;
        _index = 0;
        _cleaned = _quarantined = _purged = 0;
        _quarantinedCount = _purgedCount = _kept = 0;
        _dryRun = false;
        _cleanLine = null;
        _ranSafe = false;
        Summary = null;
        Current = null;
        Cluster = null;
        Page = null;
        Purge.Reset();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Steps));
        if (mode == TourMode.Ask)
        {
            Stage = TourStage.Asking;
            Show();
            return;
        }
        Stage = TourStage.Cleaning;
        Raise();

        if (plan is null || plan.SafeBytes > 0)
            await CleanSafeAsync(direct, skips);
        if (plan is { Units.Count: > 0 })
        {
            var cards = plan.Units.Select(u => new UnitCard(u, now, () => { })).ToList();
            var outcome = await _main.Offers.RemoveCoreAsync(cards, purge, Progress, purge ? "Plandaki öğeler kalıcı siliniyor" : "Plandaki öğeler karantinaya alınıyor");
            Count(outcome, cards, purge);
        }
        Show();
    }

    async Task CleanSafeAsync(IReadOnlyList<UnitCard> direct, IReadOnlySet<string> skips)
    {
        _ranSafe = true;
        var outcome = new CleanOutcome();
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        _meter?.Touch(_main.Backend.ScanRoot);
        try
        {
            await Progress.RunAsync("Güvenli artıklar temizleniyor", async (p, ct) =>
            {
                p.Report(new TaskStep("Önbellek ve geçici dosyalar bulunuyor", 5, null));
                var (allOptions, allTasks, found) = await CleanupViewModel.SafeChoicesAsync(_main.Backend, ct);
                names = found;
                var (options, tasks) = SafeBreakdown.Scope(allOptions, allTasks, skips);
                _skipped += allOptions.Count - options.Count + allTasks.Count - tasks.Count;
                p.Report(new TaskStep("Önbellek ve geçici dosyalar siliniyor", 30, null));
                await CleanupViewModel.SendAsync(_main.Backend, options, tasks, outcome, p, ct, stepFreed: bytes =>
                {
                    _cleaned += bytes;
                    _main.Session.AddFreed(bytes);
                });
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
        foreach (var item in outcome.FailedItems)
            if (FailureAdvice.For(names.TryGetValue(item.Path, out var name) ? name : item.Path, item.Message) is { } note)
                _problems.Add(note);
        _problems.AddRange(FailureAdvice.ForTally(outcome.Tally));
        foreach (var failure in outcome.Failures.Take(MainViewModel.toastMax))
            _main.Fail(failure);

        if (direct.Count > 0)
        {
            foreach (var card in direct)
                _meter?.Touch(card.Unit);
            var removed = await _main.Offers.RemoveCoreAsync(direct, false, Progress, "Kendiliğinden yeniden oluşan dosyalar temizleniyor");
            Note(removed);
            if (removed.Error is { } error)
                _main.Fail("İşlem yapılamadı: " + error);
            _dryRun |= removed.DryRun;
            _cleaned += removed.DryRun ? 0 : removed.Freed;
            foreach (var failure in removed.Failures.Take(MainViewModel.toastMax))
                _main.Fail(failure);
        }
    }

    void Show()
    {
        Purge.Reset();
        if (Stage != TourStage.Done && Open(_index - 1, 1) is { } next)
            Display(next);
        else
            Finish();
    }

    void Display(int index)
    {
        _index = index;
        var step = _steps[index];
        Cluster = step as TourCluster;
        Current = step as UnitCard;
        Page = step;
        Stage = TourStage.Asking;
        Raise();
    }

    int? Open(int from, int direction)
    {
        for (var i = from + direction; i >= 0 && i < _steps.Count; i += direction)
            if (!_decided.Contains(_steps[i]))
                return i;
        return null;
    }

    void Decide()
    {
        Olcum.Decided("tour");
        if (_index < _steps.Count)
            _decided.Add(_steps[_index]);
        Purge.Reset();
        if ((Open(_index, 1) ?? Open(_index, -1)) is { } next)
            Display(next);
        else
            Finish();
    }

    bool Move(int direction)
    {
        if (!IsAsking || Progress.IsRunning || Open(_index, direction) is not { } next)
            return false;
        Purge.Reset();
        Display(next);
        return true;
    }

    void OnClusterChanged()
    {
        RaiseChoice();
        Commands();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(FreedBytes));
        OnPropertyChanged(nameof(HeldBytes));
        OnPropertyChanged(nameof(FreedText));
        OnPropertyChanged(nameof(HasQuarantined));
        RaiseChoice();
        Commands();
    }

    void RaiseChoice()
    {
        OnPropertyChanged(nameof(Recommended));
        OnPropertyChanged(nameof(AllIsRecommended));
        OnPropertyChanged(nameof(SelectIsRecommended));
        OnPropertyChanged(nameof(KeepIsRecommended));
        OnPropertyChanged(nameof(AllText));
        OnPropertyChanged(nameof(SelectText));
        OnPropertyChanged(nameof(KeysText));
    }

    void Commands()
    {
        QuarantineCommand.NotifyCanExecuteChanged();
        PurgeOneCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        EndCommand.NotifyCanExecuteChanged();
        QuarantineClusterCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
    }

    bool Idle() => IsAsking && !Progress.IsRunning;

    bool CanAct() => Idle() && Current is not null;

    bool CanPurgeNow() => CanAct() && CanPurgeCurrent;

    bool CanDecide() => Idle() && (Current is not null || Cluster is not null);

    bool CanSelect() => Idle() && Cluster is not null;

    bool CanQuarantineCluster() => Idle() && Cluster is { ChosenCount: > 0 };

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Quarantine()
    {
        if (Current is not { } card)
            return;
        Purge.Reset();
        var outcome = await _main.Offers.RemoveCoreAsync([card], false, Progress, OffersViewModel.TitleFor([card], false));
        Count(outcome, [card], false);
        Decide();
    }

    [RelayCommand(CanExecute = nameof(CanPurgeNow))]
    private async Task PurgeOne()
    {
        if (Current is not { } card || card.NeverPurge || !Purge.Press(card))
            return;
        var outcome = await _main.Offers.RemoveCoreAsync([card], true, Progress, OffersViewModel.TitleFor([card], true));
        Count(outcome, [card], true);
        Decide();
    }

    [RelayCommand(CanExecute = nameof(CanQuarantineCluster))]
    private async Task QuarantineCluster()
    {
        if (Cluster is not { } cluster)
            return;
        Purge.Reset();
        var cards = cluster.Chosen.Select(i => i.Card).ToList();
        var outcome = await _main.Offers.RemoveCoreAsync(cards, false, Progress, OffersViewModel.TitleFor(cards, false));
        Count(outcome, cards, false);
        _kept += cluster.Items.Count - cards.Count;
        Decide();
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (Cluster is { } cluster)
            cluster.IsExpanded = !cluster.IsExpanded;
    }

    void Note(RemoveOutcome outcome)
    {
        if (outcome.DryRun)
            return;
        foreach (var failure in outcome.Unfinished)
            foreach (var message in failure.Messages.Distinct(StringComparer.Ordinal))
                if (FailureAdvice.For(failure.Unit.Name, message) is { } note && !_problems.Contains(note))
                    _problems.Add(note);
    }

    void Count(RemoveOutcome outcome, IReadOnlyList<UnitCard> cards, bool purge)
    {
        foreach (var card in cards)
            _meter?.Touch(card.Unit);
        Note(outcome);
        if (outcome.Error is { } error)
        {
            _main.Fail("İşlem yapılamadı: " + error);
            return;
        }
        foreach (var failure in outcome.Failures)
            _main.Fail(failure);
        _dryRun |= outcome.DryRun;
        if (outcome.DryRun)
        {
            foreach (var card in cards)
                if (purge && !card.NeverPurge)
                {
                    _purged += card.Unit.SizeBytes;
                    _purgedCount++;
                }
                else
                {
                    _quarantined += card.Unit.SizeBytes;
                    _quarantinedCount++;
                }
            return;
        }
        var moved = outcome.Moved.Select(u => u.Id).ToHashSet();
        foreach (var card in cards.Where(c => moved.Contains(c.Unit.Id)))
            if (purge && !card.NeverPurge)
                _purgedCount++;
            else
                _quarantinedCount++;
        _quarantined += outcome.Pending;
        if (purge)
            _purged += outcome.Freed;
    }

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Keep()
    {
        _kept += Cluster?.Items.Count ?? 1;
        Decide();
    }

    [RelayCommand(CanExecute = nameof(Idle))]
    private void End() => Finish();

    public bool Key(TourKey key)
    {
        if (!Idle())
            return false;
        switch (key)
        {
            case TourKey.Left:
                return Move(-1);
            case TourKey.Right:
                return Move(1);
            case TourKey.Escape:
                if (Purge.IsArmed)
                {
                    Purge.Reset();
                    return true;
                }
                return Run(KeepCommand);
            default:
                return Recommended switch
                {
                    ClusterChoice.Select => Run(SelectCommand),
                    ClusterChoice.Keep => Run(KeepCommand),
                    _ => IsClusterStep ? Run(QuarantineClusterCommand) : Run(QuarantineCommand),
                };
        }
    }

    static bool Run(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null))
            return false;
        command.Execute(null);
        return true;
    }

    void Finish()
    {
        _main.Sessions.End();
        Purge.Reset();
        foreach (var step in _steps.Where(s => !_decided.Contains(s)))
            _kept += step is TourCluster c ? c.Items.Count : 1;
        foreach (var step in _steps)
            _decided.Add(step);
        _index = _steps.Count;
        var lines = new List<string>();
        if (_plan is { } plan)
        {
            var total = FreedBytes + HeldBytes;
            lines.Add(total >= plan.Target
                ? $"Hedef {TargetPlan.Goal(plan.Target)} karşılandı: {Format.Bytes(total)} ayrıldı."
                : $"Hedef {TargetPlan.Goal(plan.Target)}: {Format.Bytes(total)} ayrıldı, {Format.Bytes(plan.Target - total)} eksik kaldı.");
        }
        if (_ranSafe && (_cleaned > 0 || _quarantinedCount + _purgedCount == 0))
            lines.Add($"Önbellek ve geçici dosyalar: {Format.Bytes(_cleaned)}");
        if (_cleanLine is { } cleanLine)
            lines.Add(cleanLine);
        if (_ranSafe && _skipped > 0)
            lines.Add($"Atladığınız {Format.Count(_skipped)} kalem temizlenmedi");
        if (_quarantinedCount > 0)
            lines.Add($"Karantinaya alınan: {_quarantinedCount} öğe, {Format.Bytes(_quarantined)}. Bu yer karantina boşalınca açılır; {AppSettings.QuarantineDays.Days} gün içinde geri alabilirsiniz.");
        if (_purgedCount > 0)
            lines.Add($"Kalıcı silinen: {_purgedCount} öğe, {Format.Bytes(_purged)}");
        if (_kept > 0)
            lines.Add($"Yerinde kalan: {_kept} öğe");
        IReadOnlyList<DriveDelta> deltas = _dryRun || _meter is null ? [] : _meter.Finish();
        _meter = null;
        Summary = deltas.Count > 0
            ? new TourSummary($"Gerçekte açılan: {Format.Bytes(SpaceMeter.Total(deltas))}", lines, _dryRun, SpaceMeter.Describe(deltas), $"Tahmini: {Format.Bytes(FreedBytes)}", [.. _problems])
            : new TourSummary($"{Format.Bytes(FreedBytes)} boşaldı", lines, _dryRun, problems: [.. _problems]);
        _ = _main.Overview.EstimateAsync();
        Current = null;
        Cluster = null;
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
