using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed class OfferFilter(string label, params UnitKind[] kinds)
{
    public string Label { get; } = label;
    public IReadOnlyList<UnitKind> Kinds { get; } = kinds;
    public bool Matches(UnitKind kind) => Kinds.Count == 0 || Kinds.Contains(kind);
}

public sealed partial class UnitCard : ObservableObject
{
    readonly Action _changed;

    public UnitCard(Unit unit, DateTimeOffset now, Action changed)
    {
        Unit = unit;
        _changed = changed;
        KindLabel = KindText.Label(unit.Kind);
        SizeText = Format.Bytes(unit.SizeBytes);
        UsageText = KindText.Usage(unit.Usage, now);
        PathText = unit.Paths.Count == 1 ? unit.Paths[0] : $"{unit.Paths[0]} ve {unit.Paths.Count - 1} yol daha";
    }

    public Unit Unit { get; }
    public string Name => Unit.Name;
    public string KindLabel { get; }
    public string SizeText { get; }
    public string UsageText { get; }
    public string PathText { get; }
    public string Reason => Unit.Reason;
    public bool HasReason => !string.IsNullOrWhiteSpace(Unit.Reason);
    public bool HasUserData => Unit.ContainsUserData;
    public bool IsBatch => Unit.Removal is RemovalMethod.Quarantine or RemovalMethod.DirectDelete;
    public bool IsExternal => !IsBatch;
    public bool IsDirect => Unit.Removal == RemovalMethod.DirectDelete;

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

    partial void OnIsSelectedChanged(bool value) => _changed();
}

public sealed partial class OffersViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly List<UnitCard> _all = [];

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
            new OfferFilter("Geliştirici", UnitKind.DevArtifact),
            new OfferFilter("Önbellek", UnitKind.Cache, UnitKind.BrowserCache),
        ];
        _selectedFilter = Filters[0];
        main.Session.SnapshotChanged += (_, _) => Load();
        Load();
    }

    public TaskProgressViewModel Progress { get; }
    public IReadOnlyList<OfferFilter> Filters { get; }
    public ObservableCollection<UnitCard> Cards { get; } = [];

    [ObservableProperty]
    private OfferFilter _selectedFilter;

    [ObservableProperty]
    private string _selectionText = "Hiçbir birim seçilmedi";

    [ObservableProperty]
    private string _selectionSize = "";

    [ObservableProperty]
    private long _selectedBytes;

    public bool HasCards => Cards.Count > 0;
    public bool IsEmpty => Cards.Count == 0 && _main.Session.HasSnapshot;
    public bool NoScan => !_main.Session.HasSnapshot;
    public string EmptyText => _all.Count == 0 ? "Önerilecek birim bulunmadı" : "Bu süzgeçte birim yok";
    public string DisabledTip => "Önce en az bir birim seçin";

    partial void OnSelectedFilterChanged(OfferFilter value) => Apply();

    public void Load()
    {
        var keep = _all.Where(c => c.IsSelected).Select(c => c.Unit.Id).ToHashSet(StringComparer.Ordinal);
        _all.Clear();
        var now = DateTimeOffset.Now;
        foreach (var unit in (_main.Session.Snapshot?.Units ?? []).OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes))
        {
            var card = new UnitCard(unit, now, Selected);
            if (card.IsBatch && keep.Contains(unit.Id))
                card.IsSelected = true;
            _all.Add(card);
        }
        Apply();
    }

    void Apply()
    {
        var visible = _all.Where(c => SelectedFilter.Matches(c.Unit.Kind)).ToList();
        for (var i = Cards.Count - 1; i >= 0; i--)
            if (!visible.Contains(Cards[i]))
                Cards.RemoveAt(i);
        for (var i = 0; i < visible.Count; i++)
        {
            if (i < Cards.Count && ReferenceEquals(Cards[i], visible[i]))
                continue;
            var at = Cards.IndexOf(visible[i]);
            if (at >= 0)
                Cards.Move(at, i);
            else
                Cards.Insert(i, visible[i]);
        }
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NoScan));
        OnPropertyChanged(nameof(EmptyText));
        Selected();
    }

    public IReadOnlyList<UnitCard> Chosen => [.. _all.Where(c => c.IsSelected && c.IsBatch)];

    void Selected()
    {
        var chosen = Chosen;
        SelectedBytes = chosen.Sum(c => c.Unit.SizeBytes);
        SelectionText = chosen.Count == 0 ? "Hiçbir birim seçilmedi" : $"{Format.Count(chosen.Count)} birim seçildi · ";
        SelectionSize = chosen.Count == 0 ? "" : Format.Bytes(SelectedBytes);
        QuarantineSelectedCommand.NotifyCanExecuteChanged();
    }

    bool CanQuarantine() => Chosen.Count > 0 && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanQuarantine))]
    private async Task QuarantineSelected()
    {
        var chosen = Chosen;
        if (chosen.Count == 0)
            return;
        var userData = chosen.Where(c => c.HasUserData).ToList();
        if (userData.Count > 0 && !await _main.ConfirmAsync(
                "Kullanıcı verisi var",
                $"{string.Join(", ", userData.Select(c => c.Name))} kayıtlar ya da belgeler içeriyor. Karantinadan geri alınabilir, yine de gözden geçirin.",
                "Kullanıcı verisiyle al", danger: false))
            return;
        var direct = chosen.Where(c => c.IsDirect).ToList();
        if (direct.Count > 0 && !await _main.ConfirmAsync(
                "Geri alınamaz silme",
                $"{string.Join(", ", direct.Select(c => c.Name))} karantinaya sığmaz ve doğrudan silinir. Bu işlem geri alınamaz.",
                "Kalıcı olarak sil"))
            return;

        var ids = new List<string>();
        var moved = new List<Unit>();
        var failures = new List<string>();
        var dryRun = false;
        long freed = 0;
        try
        {
            await Progress.RunAsync("Seçilen birimler karantinaya alınıyor", async (progress, ct) =>
            {
                foreach (var card in chosen)
                {
                    ct.ThrowIfCancellationRequested();
                    var unit = card.Unit;
                    var response = await _main.Backend.SendAsync(new WorkerRequest
                    {
                        Op = card.IsDirect ? Ops.Delete : Ops.Quarantine,
                        Paths = [.. unit.Paths],
                        UnitId = unit.Id,
                        UserApproved = true,
                        IncludeUserData = unit.ContainsUserData,
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
            _main.Notify("İşlem iptal edildi; tamamlanan birimler karantinada");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("İşlem yapılamadı: " + e.Message);
            return;
        }

        if (dryRun)
        {
            _main.Notify($"Prova: {chosen.Count} birim, {Format.Bytes(chosen.Sum(c => c.Unit.SizeBytes))} karantinaya alınacaktı; dosyalar yerinde");
            return;
        }
        if (freed > 0)
            _main.Session.AddFreed(freed);
        if (moved.Count > 0)
        {
            _main.Session.RemoveUnits(moved.Select(u => u.Id));
            var bytes = moved.Sum(u => u.SizeBytes);
            if (ids.Count > 0)
                _main.Notify($"{moved.Count} birim karantinaya alındı, {Format.Bytes(bytes)}", "Geri al", () => UndoAsync(ids, moved));
            else
                _main.Notify($"{moved.Count} birim işlendi, {Format.Bytes(bytes)}");
        }
        foreach (var failure in failures)
            _main.Fail(failure);
        _ = _main.Session.RefreshQuarantineAsync(_main);
        QuarantineSelectedCommand.NotifyCanExecuteChanged();
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
    private void ClearSelection()
    {
        foreach (var card in _all)
            card.IsSelected = false;
    }

    [RelayCommand]
    private void OpenOverview() => _main.GoTo(_main.Overview);
}
