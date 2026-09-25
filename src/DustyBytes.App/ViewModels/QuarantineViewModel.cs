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
    public string DaysText => !autoPurge ? "Sen silene dek durur" : DaysLeft == 0 ? "Süresi doldu; bir sonraki yönetici işleminde silinir" : $"{DaysLeft} gün sonra kalıcı silinir";

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

public sealed partial class QuarantineViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public QuarantineViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
        main.Session.PropertyChanged += OnSession;
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

    void Load()
    {
        var snapshot = _main.Session.Quarantine;
        var keep = Items.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        Items.Clear();
        Warnings.Clear();
        var now = DateTime.UtcNow;
        foreach (var entry in (snapshot?.Entries ?? []).OrderBy(e => e.ExpiresUtc))
            Items.Add(new QuarantineRow(entry, now, AutoPurge, Changed) { IsSelected = keep.Contains(entry.Id) });
        foreach (var usage in snapshot?.Usage ?? [])
            if (usage.Warning)
                Warnings.Add(new VolumeWarning($"{usage.Root} sürücüsünde karantina {Format.Bytes(usage.PendingBytes)} yer tutuyor; birimin yüzde yirmisini aştı. Süresi dolmadan kalıcı silmeyi düşünün."));
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
        var bytes = chosen.Sum(c => c.Entry.Size);
        if (!await _main.ConfirmAsync(
                $"{chosen.Count} öğe kalıcı silinsin mi",
                $"{Format.Bytes(bytes)} karantinadan tamamen silinir. Bu işlem geri alınamaz.",
                "Kalıcı sil"))
            return;
        var response = await SendAsync("Karantina boşaltılıyor", Ops.Purge, chosen);
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

    async Task<WorkerResponse?> SendAsync(string title, string op, List<QuarantineRow> rows)
    {
        if (rows.Count == 0)
            return null;
        try
        {
            var response = await Progress.RunAsync(title, (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = op,
                UserApproved = true,
                Items = [.. rows.Select(r => r.Id)],
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
