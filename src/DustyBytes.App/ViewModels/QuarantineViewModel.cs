using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class QuarantineRow(QuarantineEntry entry, DateTime nowUtc, bool autoPurge, Action changed) : ObservableObject
{
    public QuarantineEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Name => Path.GetFileName(Entry.OriginalPath.TrimEnd('\\')) is { Length: > 0 } n ? n : Entry.OriginalPath;
    public string OriginalPath => Entry.OriginalPath;
    public string SizeText => Format.Bytes(Entry.Size);
    public int DaysLeft { get; } = Math.Max(0, (int)Math.Ceiling((Due(entry) - nowUtc).TotalDays));
    public string DaysText => !autoPurge ? "Sen silene dek durur" : DaysLeft == 0 ? "Süresi doldu; birazdan kalıcı silinir" : $"{DaysLeft} gün sonra kalıcı silinir";

    static DateTime Due(QuarantineEntry e)
    {
        var byAge = e.MovedUtc + AppSettings.QuarantineDays;
        return byAge < e.ExpiresUtc ? byAge : e.ExpiresUtc;
    }
    public string MovedText => "Taşındı " + Entry.MovedUtc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}

public sealed record VolumeWarning(string Text);

public sealed class QuarantineGroup(string? sessionId, string title, IReadOnlyList<QuarantineRow> rows)
{
    public string? SessionId { get; } = sessionId;
    public string Title { get; } = title;
    public IReadOnlyList<QuarantineRow> Rows { get; } = rows;
    public bool IsLegacy => SessionId is null;
    public long Bytes => Rows.Sum(r => r.Entry.Size);
    public string SummaryText => $"{Format.Count(Rows.Count)} öğe, {Format.Bytes(Bytes)}";
    public string RestoreName => Title + " grubunu geri al";
}

public sealed partial class QuarantineViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public QuarantineViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
        main.Session.PropertyChanged += OnSession;
        Empty.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TwoStep.Target))
            {
                OnPropertyChanged(nameof(IsEmptyArmed));
                OnPropertyChanged(nameof(EmptyText));
            }
        };
        main.Ticked += Empty.Elapse;
    }

    public TwoStep Empty { get; } = new();
    public bool IsEmptyArmed => Empty.IsArmedFor(this);
    public string EmptyText => IsEmptyArmed ? TwoStep.ArmedText : "Karantinayı boşalt";
    public ObservableCollection<QuarantineGroup> Groups { get; } = [];

    public static string GroupTitle(DateTime movedUtc, DateTime nowLocal)
    {
        var local = movedUtc.ToLocalTime();
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var day = local.Date == nowLocal.Date ? "Bugün" : local.Date == nowLocal.Date.AddDays(-1) ? "Dün" : local.ToString("d MMMM", tr);
        return day + " " + local.ToString("HH:mm", tr);
    }

    public static List<QuarantineGroup> GroupRows(IEnumerable<QuarantineRow> rows, DateTime nowLocal)
    {
        var list = rows.ToList();
        var groups = list.Where(r => r.Entry.SessionId is not null)
            .GroupBy(r => r.Entry.SessionId!, StringComparer.Ordinal)
            .Select(g => (Moved: g.Min(r => r.Entry.MovedUtc), Rows: g.OrderByDescending(r => r.Entry.MovedUtc).ToList(), Id: g.Key))
            .OrderByDescending(g => g.Moved)
            .Select(g => new QuarantineGroup(g.Id, GroupTitle(g.Moved, nowLocal), g.Rows))
            .ToList();
        var legacy = list.Where(r => r.Entry.SessionId is null).ToList();
        if (legacy.Count > 0)
            groups.Add(new QuarantineGroup(null, "Eski kayıtlar", legacy));
        return groups;
    }

    public TaskProgressViewModel Progress { get; }
    public ObservableCollection<QuarantineRow> Items { get; } = [];
    public ObservableCollection<VolumeWarning> Warnings { get; } = [];

    [ObservableProperty]
    private string _pendingText = "";

    [ObservableProperty]
    private string _freedText = "";

    [ObservableProperty]
    private string _selectionText = "";

    [ObservableProperty]
    private string _selectionSize = "";

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private bool _autoPurge = AppSettings.Load().AutoPurge;

    public string AutoPurgeText => $"Karantinada {AppSettings.QuarantineDays.Days} günü geçenleri kalıcı sil";
    public string AutoPurgeTip => $"Açıkken, karantinada {AppSettings.QuarantineDays.Days} günü dolduran öğeler DustyBytes açıkken kendiliğinden kalıcı silinir";
    public string EmptyHint => $"Karantinaya alınanlar burada bekler; istediğin an geri alırsın. Üstteki seçenek açıksa {AppSettings.QuarantineDays.Days} günü geçenler kalıcı silinir.";

    partial void OnAutoPurgeChanged(bool value)
    {
        try
        {
            (AppSettings.Load() with { AutoPurge = value }).Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _main.Fail("Ayar kaydedilemedi: " + e.Message);
        }
        Load();
    }

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0 && _main.Session.Quarantine is { Complete: true };
    public bool Incomplete => _main.Session.Quarantine is { Complete: false };
    public bool HasWarnings => Warnings.Count > 0;
    public string DisabledTip => "Önce en az bir öğe seçin";

    List<QuarantineRow> Selected => [.. Items.Where(i => i.IsSelected)];

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.Quarantine) or nameof(SessionState.Ledger))
            Load();
    }

    protected override void OnNavigatedTo() => _ = _main.Session.RefreshQuarantineAsync(_main);

    protected override void OnNavigatedFrom() => Empty.Reset();

    void Load()
    {
        var snapshot = _main.Session.Quarantine;
        var keep = Items.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        Items.Clear();
        Warnings.Clear();
        var now = DateTime.UtcNow;
        foreach (var entry in (snapshot?.Entries ?? []).OrderBy(e => e.ExpiresUtc))
            Items.Add(new QuarantineRow(entry, now, AutoPurge, Changed) { IsSelected = keep.Contains(entry.Id) });
        Groups.Clear();
        foreach (var group in GroupRows(Items, DateTime.Now))
            Groups.Add(group);
        foreach (var usage in snapshot?.Usage ?? [])
            if (usage.Warning)
                Warnings.Add(new VolumeWarning($"{usage.Root} sürücüsünde karantina {Format.Bytes(usage.PendingBytes)} yer tutuyor; diskin beşte birini geçti. Yer lazımsa Karantinayı boşalt ile hemen açabilirsiniz."));
        PendingText = _main.Session.PendingText;
        FreedText = Format.Bytes(_main.Session.Ledger.FreedBytes);
        Note = snapshot?.Note ?? "";
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Incomplete));
        OnPropertyChanged(nameof(HasWarnings));
        Changed();
    }

    void Changed()
    {
        var selected = Selected;
        SelectionText = selected.Count == 0 ? "Hiçbir öğe seçilmedi" : $"{Format.Count(selected.Count)} öğe seçildi · ";
        SelectionSize = selected.Count == 0 ? "" : Format.Bytes(selected.Sum(s => s.Entry.Size));
        RestoreCommand.NotifyCanExecuteChanged();
        PurgeCommand.NotifyCanExecuteChanged();
        EmptyAllCommand.NotifyCanExecuteChanged();
    }

    bool CanAct() => !Progress.IsRunning && Items.Any(i => i.IsSelected);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Restore()
    {
        var chosen = Selected;
        var response = await SendAsync("Seçilenler geri alınıyor", Ops.Restore, chosen);
        if (response is null)
            return;
        if (response.Ok)
            _main.Notify($"{chosen.Count} öğe yerine döndü");
        else
            _main.Fail("Geri alma tamamlanamadı: " + response.Message);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Purge()
    {
        var chosen = Selected;
        var response = await SendAsync("Seçilenler siliniyor", Ops.Purge, chosen);
        if (response is null)
            return;
        if (response.DryRun)
        {
            _main.Notify("Prova: hiçbir öğe silinmedi");
            return;
        }
        if (response.FreedBytes > 0)
            _main.Session.AddFreed(response.FreedBytes);
        if (response.Ok)
            _main.Notify($"{chosen.Count} öğe silindi, {Format.Bytes(response.FreedBytes)} açıldı");
        else
            _main.Fail("Silme tamamlanamadı: " + response.Message);
    }

    bool CanEmpty() => !Progress.IsRunning && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanEmpty))]
    private async Task EmptyAll()
    {
        if (!Empty.Press(this))
            return;
        var response = await SendAsync("Karantina boşaltılıyor", Ops.Purge, [.. Items], Targets.All);
        if (response is null)
            return;
        if (response.DryRun)
        {
            _main.Notify("Prova: hiçbir öğe silinmedi");
            return;
        }
        if (response.FreedBytes > 0)
            _main.Session.AddFreed(response.FreedBytes);
        if (response.Ok)
            _main.Notify($"Karantina boşaltıldı, {Format.Bytes(response.FreedBytes)} açıldı");
        else
            _main.Fail("Karantinanın bir kısmı silinemedi: " + response.Message);
    }

    [RelayCommand]
    private async Task RestoreGroup(QuarantineGroup? group)
    {
        if (group is null || Progress.IsRunning)
            return;
        var response = group.SessionId is { } id
            ? await RestoreSessionAsync(id, Progress)
            : await SendAsync("Eski kayıtlar geri alınıyor", Ops.Restore, [.. group.Rows]);
        if (response is null)
            return;
        if (response.Ok)
            _main.Notify($"{group.Title}: {Format.Count(group.Rows.Count)} öğe yerine döndü");
        else
            _main.Fail("Geri alma tamamlanamadı: " + response.Message);
    }

    public async Task<WorkerResponse?> RestoreSessionAsync(string sessionId, TaskProgressViewModel runner)
    {
        try
        {
            var response = await runner.RunAsync("Oturum geri alınıyor", (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = Ops.Restore,
                UserApproved = true,
                SessionId = sessionId,
            }, p, ct), cancellable: false);
            await _main.Session.RefreshQuarantineAsync(_main);
            return response;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Geri alma yapılamadı: " + e.Message);
            return null;
        }
        finally
        {
            Changed();
        }
    }

    async Task<WorkerResponse?> SendAsync(string title, string op, List<QuarantineRow> rows, string? target = null)
    {
        if (rows.Count == 0)
            return null;
        try
        {
            var response = await Progress.RunAsync(title, (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = op,
                UserApproved = true,
                Target = target,
                Items = target is null ? [.. rows.Select(r => r.Id)] : [],
            }, p, ct), cancellable: false);
            await _main.Session.RefreshQuarantineAsync(_main);
            return response;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("İşlem yapılamadı: " + e.Message);
            return null;
        }
        finally
        {
            Changed();
        }
    }

    [RelayCommand]
    private async Task OpenElevated()
    {
        try
        {
            await Progress.RunAsync("Karantina yönetici izniyle okunuyor", async (p, ct) =>
            {
                await _main.Backend.SendAsync(new WorkerRequest { Op = Ops.Ping }, p, ct);
                return true;
            }, cancellable: false);
            await _main.Session.RefreshQuarantineAsync(_main);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Yönetici izni alınamadı: " + e.Message);
        }
    }

    [RelayCommand]
    private void OpenOffers() => _main.GoTo(_main.Offers);

    [RelayCommand]
    private void SelectAll()
    {
        var all = !Items.All(i => i.IsSelected);
        foreach (var item in Items)
            item.IsSelected = all;
    }
}
