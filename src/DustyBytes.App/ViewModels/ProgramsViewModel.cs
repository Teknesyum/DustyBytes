using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class ProgramRow(ProgramInfo info, DateTimeOffset now) : ObservableObject
{
    public ProgramInfo Info { get; } = info;
    public string Name => Info.Program.DisplayName;
    public string Publisher => Info.Program.Publisher ?? "Yayıncı bilinmiyor";
    public long ShownBytes => Info.Program.SizeBytes > 0 ? Info.Program.SizeBytes : Info.Program.EstimatedSizeBytes;
    public string SizeText => Info.Program.SizeBytes > 0 ? Format.Bytes(Info.Program.SizeBytes) : Info.Program.EstimatedSizeBytes > 0 ? Format.Bytes(Info.Program.EstimatedSizeBytes) : "Boyut bilinmiyor";
    public string UsageText { get; } = KindText.Usage(info.Usage, now);
    public string VersionText => Info.Program.DisplayVersion is { Length: > 0 } v ? "Sürüm " + v : "";
    public bool CanUninstall => Info.Program.CanUninstall;
    public bool UninstallerMissing => Info.Program.UninstallerMissing;
    public bool CanForce => UninstallerMissing && ForceUninstall.Refusal(Info.Program) is null;
    public bool CanOpen => CanUninstall || CanForce;
    public bool CanCheck => CanUninstall && !UninstallerMissing;

    public bool CanForceAfter(bool vendorFailed, bool uninstalled) =>
        ForceUninstall.Refusal(Info.Program) is null && (UninstallerMissing && !uninstalled || vendorFailed);

    public Action? Changed { get; set; }

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => Changed?.Invoke();
}

public sealed partial class ProgramsViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    List<ProgramRow> _all = [];
    bool _loaded;

    public ProgramsViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
        Progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskProgressViewModel.IsRunning))
                Raise();
        };
    }

    public TaskProgressViewModel Progress { get; }
    public ObservableCollection<ProgramRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyPropertyChangedFor(nameof(UninstallTip))]
    private ProgramRow? _selected;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private string _countText = "";

    public bool IsLoading => Progress.IsRunning;
    public bool HasRows => Rows.Count > 0;
    public bool IsEmpty => _loaded && Rows.Count == 0 && Error is null && !IsLoading;
    public bool HasError => Error is not null && !IsLoading;
    public int CheckedCount => _all.Count(r => r.IsChecked);
    public bool HasChecked => CheckedCount > 0;
    public bool ShowSingle => !HasChecked;
    public string BulkText => $"Seçilenleri kaldır ({CheckedCount})";

    public string UninstallTip => Selected switch
    {
        _ when HasChecked => $"{Format.Count(CheckedCount)} program sırayla, sessiz kaldırılır; önce onay sorulur",
        null => "Önce listeden bir program seçin; birden çok program için kutuları işaretleyin",
        { CanOpen: false } => "Bu program için kaldırma komutu yok",
        { CanForce: true } => "Kaldırıcısı bulunamadı; açılan sayfada zorla kaldırma önerilir",
        _ => "Kalıntı önizlemesini açar; henüz hiçbir şey silinmez",
    };

    partial void OnQueryChanged(string value) => Apply();

    protected override void OnNavigatedTo()
    {
        if (!_loaded && !Progress.IsRunning)
            _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Error = null;
        Raise();
        try
        {
            var list = await Progress.RunAsync("Kurulu programlar okunuyor", (p, ct) => _main.Backend.ListProgramsAsync(p, ct));
            var now = DateTimeOffset.Now;
            _all = [.. list.Select(i => new ProgramRow(i, now) { Changed = CheckedChanged }).OrderByDescending(r => r.ShownBytes)];
            _loaded = true;
            Apply();
        }
        catch (OperationCanceledException)
        {
            Error = "Okuma iptal edildi";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = "Program listesi okunamadı: " + e.Message;
        }
        Raise();
    }

    void Apply()
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => Query.Length == 0 || r.Name.Contains(Query, StringComparison.CurrentCultureIgnoreCase) || r.Publisher.Contains(Query, StringComparison.CurrentCultureIgnoreCase)))
            Rows.Add(row);
        CountText = $"{Format.Count(Rows.Count)} program";
        if (Selected is not null && !Rows.Contains(Selected))
            Selected = null;
        Raise();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
        CheckedChanged();
    }

    void CheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(ShowSingle));
        OnPropertyChanged(nameof(BulkText));
        OnPropertyChanged(nameof(UninstallTip));
        BulkUninstallCommand.NotifyCanExecuteChanged();
    }

    public void Removed(ProgramRow row)
    {
        _all.Remove(row);
        Apply();
    }

    bool CanUninstall() => Selected is { CanOpen: true };

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void Uninstall()
    {
        if (Selected is not { } row)
            return;
        var page = new UninstallViewModel(_main, this, row);
        _main.Navigation.Push(page);
    }

    bool CanBulk() => HasChecked && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanBulk))]
    private async Task BulkUninstall()
    {
        var rows = _all.Where(r => r.IsChecked).ToList();
        if (rows.Count == 0)
            return;
        var body = "Programlar sırayla, kendi kaldırıcılarıyla sessiz kaldırılır. İlkinden önce bir geri yükleme noktası oluşturulur. "
            + "Her programdan sonra kesin kalıntılar kayıt yedeği alınıp 7 gün karantinada tutularak temizlenir. "
            + "Sessiz kaldırılamayan programın penceresini siz bitirirsiniz; iptal ederseniz o anki program bitince sıra durur.";
        if (!await _main.ConfirmAsync($"{Format.Count(rows.Count)} program kaldırılsın mı?", body, "Sırayla kaldır"))
            return;
        foreach (var row in rows)
            row.IsChecked = false;
        _main.Navigation.Push(new BulkUninstallViewModel(_main, this, rows));
    }
}
