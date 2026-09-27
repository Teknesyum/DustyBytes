using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed class OfferFilter(string label, params UnitKind[] kinds)
{
    public string Label { get; } = label;
    public IReadOnlyList<UnitKind> Kinds { get; } = kinds;
    public bool Matches(UnitKind kind) => Kinds.Count == 0 || Kinds.Contains(kind);
}

public sealed class BulkCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed partial class UnitCard : ObservableObject
{
    readonly Action _changed;

    public UnitCard(Unit unit, DateTimeOffset now, Action changed, bool selected = false, bool settled = true)
    {
        _unit = unit;
        _changed = changed;
        _isSettled = settled;
        _isSelected = selected && settled;
        KindLabel = KindText.Label(unit);
        Effect = KindText.Effect(unit);
        UsageText = KindText.Usage(unit.Usage, now);
        PathText = Describe(unit);
    }

    static string Describe(Unit unit) => unit.Paths.Count == 1 ? unit.Paths[0] : $"{unit.Paths[0]} ve {unit.Paths.Count - 1} yol daha";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private Unit _unit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(IsPending), nameof(IsBatch), nameof(CanPurge))]
    private bool _isSettled;

    public bool IsPending => !IsSettled;

    public void Settle(Unit unit, bool settled, DateTimeOffset now)
    {
        Unit = unit;
        UsageText = KindText.Usage(unit.Usage, now);
        PathText = Describe(unit);
        IsSettled = settled;
        if (!settled)
            IsSelected = false;
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(PathText));
    }

    public string Name => Unit.Name;
    public string KindLabel { get; }
    public string Effect { get; }
    public string ActionText => IsDirect ? "Temizle" : "Karantinaya al";
    public string ActionHint => IsDirect
        ? "Kendiliğinden yeniden oluşan dosyalar; hemen silinir"
        : $"Hemen yer açılır; {AppSettings.QuarantineDays.Days} gün içinde istediğin an geri alırsın";
    public string SizeText => IsSettled ? Format.Bytes(Unit.SizeBytes) : "—";
    public string UsageText { get; private set; }
    public string PathText { get; private set; }
    public string Reason => Unit.Reason;
    public bool HasReason => !string.IsNullOrWhiteSpace(Unit.Reason);
    public bool HasUserData => Unit.ContainsUserData;
    public bool IsRemovable => Unit.Removal is RemovalMethod.Quarantine or RemovalMethod.DirectDelete;
    public bool IsBatch => IsRemovable && IsSettled;
    public bool IsExternal => !IsRemovable;
    public bool IsDirect => Unit.Removal == RemovalMethod.DirectDelete;
    public bool CanPurge => IsBatch && !IsDirect;
    public string PurgeHint => "Karantinaya almadan siler; geri alınamaz";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PurgeText))]
    private bool _isPurgeArmed;

    public string PurgeText => IsPurgeArmed ? TwoStep.ArmedText : "Kalıcı sil";

    public string ExternalText => Unit.Removal switch
    {
        RemovalMethod.Launcher => "Başlatıcıda aç",
        RemovalMethod.Uninstaller => "Programlarda kaldır",
        _ => "Temizlikte aç",
    };

    public string ExternalHint => Unit.Removal switch
    {
        RemovalMethod.Launcher => "Oyunu kendi başlatıcısı kaldırır; kütüphane bozulmaz",
        RemovalMethod.Uninstaller => "Programı kendi kaldırıcısı kaldırır, kalıntı önizlemesiyle",
        _ => "Bu alanı Windows aracı temizler",
    };

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !IsBatch)
        {
            IsSelected = false;
            return;
        }
        _changed();
    }
}

public sealed partial class OffersViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    List<UnitCard> _all = [];
    bool _stale = true;
    int _generation;

    public OffersViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
        Filters =
        [
            new OfferFilter("Tümü"),
            new OfferFilter("Oyun", UnitKind.Game),
            new OfferFilter("Film", UnitKind.Film, UnitKind.Series),
            new OfferFilter("Program", UnitKind.Program),
            new OfferFilter("Uygulama içeriği", UnitKind.AppContent),
            new OfferFilter("Geliştirici", UnitKind.DevArtifact),
            new OfferFilter("Önbellek", UnitKind.Cache, UnitKind.BrowserCache),
        ];
        _selectedFilter = Filters[0];
        main.Session.SnapshotChanged += (_, _) => Invalidate();
        main.Session.DraftChanged += (_, _) => Stream();
        Purge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TwoStep.Target))
                Armed();
        };
        main.Ticked += Purge.Elapse;
    }

    static readonly object BarKey = new();
    UnitCard? _armedCard;

    public TwoStep Purge { get; } = new();
    public bool IsBarArmed => Purge.IsArmedFor(BarKey);
    public string PurgeBarText => IsBarArmed ? TwoStep.ArmedText : "Seçilenleri kalıcı sil";

    void Armed()
    {
        if (_armedCard is not null)
            _armedCard.IsPurgeArmed = false;
        _armedCard = Purge.Target as UnitCard;
        if (_armedCard is not null)
            _armedCard.IsPurgeArmed = true;
        OnPropertyChanged(nameof(IsBarArmed));
        OnPropertyChanged(nameof(PurgeBarText));
    }

    public void Disarm() => Purge.Reset();

    protected override void OnNavigatedFrom() => Purge.Reset();

    bool _streaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    public bool HasNotice => Notice.Length > 0;

    bool Visible(UnitCard card) => SelectedFilter.Matches(card.Unit.Kind) && (ShowSmall || !IsSmall(card));

    void Stream()
    {
        var draft = _main.Session.Draft;
        if (_main.Session.HasSnapshot)
            return;
        if (draft is null)
        {
            if (!_streaming)
                return;
            _streaming = false;
            Notice = "";
            _all = [];
            Apply();
            return;
        }
        if (!_streaming)
        {
            _streaming = true;
            _generation++;
            _all = [];
            IsLoading = false;
            if (Cards.Count > 0)
                Cards.ReplaceAll([]);
            Notice = "Tarama sürüyor. Film, dizi, geliştirici artığı ve büyük klasörler tarama bitince eklenir.";
        }
        var now = DateTimeOffset.Now;
        var ids = draft.Units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _all.Where(c => !ids.Contains(c.Unit.Id)).ToList())
        {
            _all.Remove(gone);
            Cards.Remove(gone);
        }
        var byId = _all.ToDictionary(c => c.Unit.Id, StringComparer.Ordinal);
        foreach (var unit in draft.Units)
        {
            var settled = !draft.Pending.Contains(unit.Id);
            if (byId.TryGetValue(unit.Id, out var card))
            {
                card.Settle(unit, settled, now);
                var shown = Cards.Contains(card);
                if (Visible(card) && !shown)
                    Cards.Add(card);
                else if (!Visible(card) && shown)
                    Cards.Remove(card);
            }
            else
            {
                card = new UnitCard(unit, now, Selected, settled: settled);
                _all.Add(card);
                if (Visible(card))
                    Cards.Add(card);
            }
        }
        RaiseState();
        Selected();
    }

    public TaskProgressViewModel Progress { get; }
    public IReadOnlyList<OfferFilter> Filters { get; }
    public BulkCollection<UnitCard> Cards { get; } = [];
    public Task Ready { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingText = "";

    [ObservableProperty]
    private OfferFilter _selectedFilter;

    [ObservableProperty]
    private string _selectionText = "Hiçbir birim seçilmedi";

    [ObservableProperty]
    private string _selectionSize = "";

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private bool _showSmall;

    public const long SmallBytes = 1L << 30;
    public bool HasSmall => SmallCount > 0;
    int SmallCount => _all.Count(c => IsSmall(c) && SelectedFilter.Matches(c.Unit.Kind));
    public string SmallText => $"1 GB altındakileri de göster · {Format.Count(SmallCount)} birim, {Format.Bytes(_all.Where(c => IsSmall(c) && SelectedFilter.Matches(c.Unit.Kind)).Sum(c => c.Unit.SizeBytes))}";

    static bool IsSmall(UnitCard card) => card.Unit.SizeBytes < SmallBytes;

    partial void OnShowSmallChanged(bool value) => Apply();

    public bool HasCards => Cards.Count > 0 && !IsLoading;
    public bool IsEmpty => Cards.Count == 0 && !IsLoading && _main.Session.HasSnapshot;
    public bool NoScan => !_main.Session.HasSnapshot && !_streaming;
    public string EmptyText => _all.Count == 0 ? "Önerilecek birim bulunmadı" : HasSmall && !ShowSmall ? "1 GB üstünde birim yok" : "Bu süzgeçte birim yok";
    public string DisabledTip => "Önce en az bir birim seçin";

    partial void OnSelectedFilterChanged(OfferFilter value) => Apply();

    protected override void OnNavigatedTo()
    {
        if (_stale && (_main.Session.HasSnapshot || !_streaming))
            Ready = RefreshAsync();
    }

    void Invalidate()
    {
        _stale = true;
        if (IsActive)
            Ready = RefreshAsync();
        else
            RaiseState();
    }

    async Task RefreshAsync()
    {
        _stale = false;
        var generation = ++_generation;
        var units = _main.Session.Snapshot?.Units ?? [];
        var keep = _all.Where(c => c.IsSelected).Select(c => c.Unit.Id).ToHashSet(StringComparer.Ordinal);
        if (_all.Count == 0 && units.Count > 0)
        {
            LoadingText = $"{Format.Count(units.Count)} birim sıralanıyor";
            IsLoading = true;
            RaiseState();
        }
        var built = await Task.Run(() => Build(units, keep));
        if (generation != _generation)
            return;
        _all = built;
        IsLoading = false;
        if (_streaming && _main.Session.HasSnapshot)
        {
            _streaming = false;
            Notice = "Tarama bitti; sıralama güncellendi.";
        }
        else if (!_streaming)
        {
            Notice = "";
        }
        Apply();
    }

    List<UnitCard> Build(IReadOnlyList<Unit> units, HashSet<string> keep)
    {
        var now = DateTimeOffset.Now;
        var cards = new List<UnitCard>(units.Count);
        foreach (var unit in units.OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes))
        {
            var batch = unit.Removal is RemovalMethod.Quarantine or RemovalMethod.DirectDelete;
            cards.Add(new UnitCard(unit, now, Selected, batch && keep.Contains(unit.Id)));
        }
        return cards;
    }

    void Apply()
    {
        Cards.ReplaceAll(_all.Where(c => SelectedFilter.Matches(c.Unit.Kind) && (ShowSmall || !IsSmall(c))));
        RaiseState();
        Selected();
    }

    void RaiseState()
    {
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NoScan));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasSmall));
        OnPropertyChanged(nameof(SmallText));
    }

    public IReadOnlyList<UnitCard> Chosen => [.. _all.Where(c => c.IsSelected && c.IsBatch)];

    bool _bulk;

    public bool HasBatch => Cards.Any(c => c.IsBatch);

    public bool? AllSelected
    {
        get
        {
            var picked = 0;
            var total = 0;
            foreach (var card in Cards)
            {
                if (!card.IsBatch)
                    continue;
                total++;
                if (card.IsSelected)
                    picked++;
            }
            return picked == 0 ? false : picked == total ? true : null;
        }
    }

    [RelayCommand]
    private void ToggleAll()
    {
        var all = AllSelected == true;
        SetMany(Cards.Where(c => c.IsBatch), !all);
    }

    void SetMany(IEnumerable<UnitCard> cards, bool value)
    {
        _bulk = true;
        try
        {
            foreach (var card in cards.ToList())
                card.IsSelected = value;
        }
        finally
        {
            _bulk = false;
        }
        Selected();
    }

    void Selected()
    {
        if (_bulk)
            return;
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(HasBatch));
        var chosen = Chosen;
        SelectedBytes = chosen.Sum(c => c.Unit.SizeBytes);
        SelectionText = chosen.Count == 0 ? "Hiçbir birim seçilmedi" : $"{Format.Count(chosen.Count)} birim seçildi · ";
        SelectionSize = chosen.Count == 0 ? "" : Format.Bytes(SelectedBytes);
        if (IsBarArmed)
            Purge.Reset();
        QuarantineSelectedCommand.NotifyCanExecuteChanged();
        PurgeSelectedCommand.NotifyCanExecuteChanged();
    }

    bool CanQuarantine() => Chosen.Count > 0 && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanQuarantine))]
    private Task QuarantineSelected() => RemoveAsync(Chosen);

    [RelayCommand]
    private Task RemoveOne(UnitCard card) => card.IsBatch && !Progress.IsRunning ? RemoveAsync([card]) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanQuarantine))]
    private Task PurgeSelected()
    {
        var chosen = Chosen;
        if (chosen.Count == 0 || !Purge.Press(BarKey))
            return Task.CompletedTask;
        return RemoveAsync(chosen, purge: true);
    }

    [RelayCommand]
    private Task PurgeOne(UnitCard card)
    {
        if (!card.CanPurge || Progress.IsRunning || !Purge.Press(card))
            return Task.CompletedTask;
        return RemoveAsync([card], purge: true);
    }

    async Task RemoveAsync(IReadOnlyList<UnitCard> chosen, bool purge = false)
    {
        if (chosen.Count == 0)
            return;
        var title = chosen.Count == 1
            ? (purge ? chosen[0].Name + " kalıcı siliniyor" : chosen[0].IsDirect ? chosen[0].Name + " temizleniyor" : chosen[0].Name + " karantinaya alınıyor")
            : purge ? "Seçilen birimler kalıcı siliniyor" : "Seçilen birimler karantinaya alınıyor";

        var ids = new List<string>();
        var moved = new List<Unit>();
        var failures = new List<string>();
        var dryRun = false;
        long freed = 0;
        try
        {
            await Progress.RunAsync(title, async (progress, ct) =>
            {
                foreach (var card in chosen)
                {
                    ct.ThrowIfCancellationRequested();
                    var unit = card.Unit;
                    var response = await _main.Backend.SendAsync(new WorkerRequest
                    {
                        Op = purge || card.IsDirect ? Ops.Delete : Ops.Quarantine,
                        Paths = [.. unit.Paths],
                        UnitId = unit.Id,
                        UserApproved = true,
                        IncludeUserData = !card.IsDirect || unit.ContainsUserData,
                    }, progress, ct);
                    dryRun |= response.DryRun;
                    freed += response.FreedBytes;
                    var unitIds = IdsOf(response);
                    ids.AddRange(unitIds);
                    if (response.Ok)
                        moved.Add(unit);
                    else
                        failures.Add($"{unit.Name}: {response.Message}");
                }
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            _main.Notify(purge ? "İşlem iptal edildi; tamamlanan birimler silindi" : "İşlem iptal edildi; tamamlanan birimler karantinada");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("İşlem yapılamadı: " + e.Message);
            return;
        }

        if (dryRun)
        {
            _main.Notify($"Prova: {chosen.Count} birim, {Format.Bytes(chosen.Sum(c => c.Unit.SizeBytes))} {(purge ? "kalıcı silinecekti" : "karantinaya alınacaktı")}; dosyalar yerinde");
            return;
        }
        if (freed > 0)
            _main.Session.AddFreed(freed);
        if (moved.Count > 0)
        {
            _main.Session.RemoveUnits(moved.Select(u => u.Id));
            var bytes = moved.Sum(u => u.SizeBytes);
            var what = moved.Count == 1 ? moved[0].Name : $"{moved.Count} birim";
            if (purge)
                _main.Notify($"{what} kalıcı silindi, {Format.Bytes(bytes)} yer açıldı");
            else if (ids.Count > 0)
                _main.Notify($"{what} karantinada, {Format.Bytes(bytes)} yer açıldı. {AppSettings.QuarantineDays.Days} gün sonra kendiliğinden silinir.", "Geri al", () => UndoAsync(ids, moved));
            else
                _main.Notify($"{what} temizlendi, {Format.Bytes(bytes)} yer açıldı");
        }
        foreach (var failure in failures)
            _main.Fail(failure);
        _ = _main.Session.RefreshQuarantineAsync(_main);
        QuarantineSelectedCommand.NotifyCanExecuteChanged();
        PurgeSelectedCommand.NotifyCanExecuteChanged();
    }

    public static List<string> IdsOf(WorkerResponse response) =>
        [.. response.Items.Where(i => i.Ok).Select(i => i.Path.LastIndexOf('|') is var at && at >= 0 ? i.Path[(at + 1)..] : "").Where(id => id.Length > 0)];

    async Task UndoAsync(List<string> ids, List<Unit> units)
    {
        try
        {
            var response = await Progress.RunAsync("Karantinadan geri alınıyor", (progress, ct) =>
                _main.Backend.SendAsync(new WorkerRequest { Op = Ops.Restore, Items = ids, UserApproved = true }, progress, ct), cancellable: false);
            if (response.Ok)
            {
                _main.Session.RestoreUnits(units);
                _main.Notify($"{units.Count} birim yerine döndü");
            }
            else
            {
                _main.Fail("Geri alma tamamlanamadı: " + response.Message);
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Geri alma yapılamadı: " + e.Message);
        }
        _ = _main.Session.RefreshQuarantineAsync(_main);
    }

    [RelayCommand]
    private async Task External(UnitCard card)
    {
        switch (card.Unit.Removal)
        {
            case RemovalMethod.Launcher when card.Unit.LauncherUri is { } uri:
                await _main.OpenUri(uri);
                break;
            case RemovalMethod.Uninstaller:
                _main.GoTo(_main.Programs);
                break;
            default:
                _main.GoTo(_main.Cleanup);
                break;
        }
    }

    [RelayCommand]
    private void ClearSelection() => SetMany(_all, false);

    [RelayCommand]
    private void OpenOverview() => _main.GoTo(_main.Overview);
}
