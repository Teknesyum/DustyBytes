using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public static class KindText
{
    public static string Label(UnitKind kind) => kind switch
    {
        UnitKind.Game => "Oyun",
        UnitKind.Program => "Program",
        UnitKind.Film => "Film",
        UnitKind.Series => "Dizi",
        UnitKind.DevArtifact => "Geliştirici",
        UnitKind.Cache => "Önbellek",
        UnitKind.BrowserCache => "Tarayıcı önbelleği",
        UnitKind.Installer => "Kurulum dosyası",
        UnitKind.SystemArtifact => "Sistem artığı",
        _ => "Klasör",
    };

    public static string Usage(UsageSignal usage, DateTimeOffset now) =>
        usage.LastUsed is { } last ? "Son kullanım " + Format.Ago(last, now) : "Kullanım bilgisi yok";
}

public sealed record KindBar(string Label, string SizeText, double Share, UnitKind Kind);

public sealed record TopUnit(string Name, string KindLabel, string SizeText, string UsageText);

public sealed partial class OverviewViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        Session.PropertyChanged += OnSession;
        Session.Scan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskProgressViewModel.IsRunning))
                Raise();
        };
    }

    public SessionState Session => _main.Session;
    public TaskProgressViewModel Progress => Session.Scan;

    public ObservableCollection<KindBar> Bars { get; } = [];
    public ObservableCollection<TopUnit> Top { get; } = [];

    [ObservableProperty]
    private string _heroText = "";

    [ObservableProperty]
    private string _unitCountText = "";

    [ObservableProperty]
    private string _lastScanText = "";

    [ObservableProperty]
    private string _pendingText = "";

    [ObservableProperty]
    private string _freedText = "";

    [ObservableProperty]
    private string _scannedText = "";

    public bool HasSnapshot => Session.HasSnapshot;
    public bool IsRefreshing => Session.IsRefreshing;
    public bool IsScanning => Session.Scan.IsRunning;
    public bool HasError => Session.ScanError is not null && !Session.HasSnapshot && !IsScanning;
    public string ErrorText => Session.ScanError ?? "";
    public bool IsEmpty => !Session.HasSnapshot && !IsScanning && Session.ScanError is null;
    public bool ShowContent => Session.HasSnapshot;
    public string PrimaryText => HasError ? "Yeniden dene" : Session.HasSnapshot ? "Yeniden tara" : "Taramayı başlat";

    public Availability FastScanState => _main.Backend.FastScanAvailability();
    public bool FastScanEnabled => FastScanState.Enabled && !IsScanning;
    public string FastScanTip => FastScanState.Reason;

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionState.Snapshot):
                Rebuild();
                break;
            case nameof(SessionState.Quarantine):
            case nameof(SessionState.Ledger):
                Counters();
                break;
        }
        Raise();
    }

    public void Raise()
    {
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(FastScanEnabled));
        StartScanCommand.NotifyCanExecuteChanged();
        FastScanCommand.NotifyCanExecuteChanged();
    }

    void Rebuild()
    {
        Bars.Clear();
        Top.Clear();
        var snapshot = Session.Snapshot;
        if (snapshot is null)
        {
            HeroText = "";
            UnitCountText = "";
            LastScanText = "";
            ScannedText = "";
            return;
        }
        var units = snapshot.Units;
        var total = units.Sum(u => u.SizeBytes);
        HeroText = Format.Bytes(total);
        UnitCountText = $"{Format.Count(units.Count)} birim";
        var now = DateTimeOffset.Now;
        LastScanText = $"Son tarama {Format.Ago(snapshot.FinishedAt, now)}, {snapshot.FinishedAt.ToLocalTime():HH:mm}";
        ScannedText = $"{Format.Count(snapshot.Result.Files)} dosya tarandı";
        var groups = units.GroupBy(u => Group(u.Kind)).Select(g => (Kind: g.Key, Bytes: g.Sum(u => u.SizeBytes))).OrderByDescending(g => g.Bytes).ToList();
        var max = groups.Count == 0 ? 1 : Math.Max(1, groups[0].Bytes);
        foreach (var g in groups)
            Bars.Add(new KindBar(KindText.Label(g.Kind), Format.Bytes(g.Bytes), (double)g.Bytes / max, g.Kind));
        foreach (var u in units.OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes).Take(5))
            Top.Add(new TopUnit(u.Name, KindText.Label(u.Kind), Format.Bytes(u.SizeBytes), KindText.Usage(u.Usage, now)));
        Counters();
    }

    static UnitKind Group(UnitKind kind) => kind switch
    {
        UnitKind.Series => UnitKind.Film,
        UnitKind.BrowserCache => UnitKind.Cache,
        _ => kind,
    };

    void Counters()
    {
        PendingText = Session.PendingText;
        FreedText = Format.Bytes(Session.Ledger.FreedBytes);
    }

    protected override void OnNavigatedTo()
    {
        Counters();
        Raise();
        _ = Session.RefreshQuarantineAsync(_main);
    }

    bool CanScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task StartScan()
    {
        var task = Session.RunScanAsync(_main, fast: false);
        Raise();
        await task;
        Raise();
    }

    bool CanFastScan() => FastScanEnabled;

    [RelayCommand(CanExecute = nameof(CanFastScan))]
    private async Task FastScan()
    {
        var task = Session.RunScanAsync(_main, fast: true);
        Raise();
        await task;
        Raise();
    }

    [RelayCommand]
    private void OpenOffers() => _main.GoTo(_main.Offers);
}
