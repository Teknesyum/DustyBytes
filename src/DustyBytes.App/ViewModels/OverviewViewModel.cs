using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.App.ViewModels;

public static class KindText
{
    public static string Label(UnitKind kind) => kind switch
    {
        UnitKind.Game => "Oyun",
        UnitKind.Program => "Program",
        UnitKind.AppContent => "Uygulama içeriği",
        UnitKind.Film => "Film",
        UnitKind.Series => "Dizi",
        UnitKind.DevArtifact => "Geliştirici",
        UnitKind.Cache => "Önbellek",
        UnitKind.BrowserCache => "Tarayıcı önbelleği",
        UnitKind.Installer => "Kurulum dosyası",
        UnitKind.SystemArtifact => "Sistem artığı",
        UnitKind.OldDownload => "Eski indirme",
        UnitKind.CloudCopy => "Bulut kopyası",
        UnitKind.Duplicate => "Kopya",
        _ => "Klasör",
    };

    public static string Label(Unit unit) => unit.Label ?? Label(unit.Kind);

    public static string Effect(Unit unit) => unit.Effect.Length > 0 ? unit.Effect : unit.Kind switch
    {
        UnitKind.Game => "Oyun kaldırılır. Kayıtlı oyunlarınız çoğunlukla bulutta durur; istediğinizde mağazadan yeniden indirirsiniz.",
        UnitKind.Program => "Program kaldırılır. Gerekirse sitesinden yeniden kurarsınız.",
        UnitKind.AppContent => "Uygulama bu içeriği kaybeder; gerekirse yeniden indirirsiniz. Uygulamanın kendisi çalışmaya devam eder.",
        UnitKind.Film or UnitKind.Series => "Dosya bilgisayardan gider. Başka yerde kopyası yoksa bir daha izleyemezsiniz.",
        UnitKind.DevArtifact => "Proje bir sonraki derlemede bunu kendiliğinden yeniden üretir. Kodunuza dokunulmaz.",
        UnitKind.Cache or UnitKind.BrowserCache => "Hiçbir şey kaybolmaz; uygulamalar ihtiyaç duydukça yeniden oluşturur.",
        UnitKind.Installer => "Program zaten kuruluysa etkisi yok. Yeniden kurmak isterseniz dosyayı tekrar indirirsiniz.",
        UnitKind.SystemArtifact => "Windows'un eski güncelleme ve kurulum artıkları temizlenir. Bilgisayar olduğu gibi çalışır.",
        UnitKind.OldDownload => "Dosyalar karantinaya taşınır, kalıcı silinmez. Lazım olan varsa 7 gün içinde karantinadan geri alırsınız.",
        UnitKind.CloudCopy => "Dosyalar bulutta kalır, yalnız bu bilgisayardaki kopyası kalkar. Açtığında internetten yeniden iner; bağlantı yoksa açılmaz.",
        UnitKind.Duplicate => DustyBytes.Units.DuplicateUnits.Effect,
        _ => "Uzun süredir açılmamış bir klasör. İçinde lazım olan bir şey varsa 7 gün içinde karantinadan geri alırsınız.",
    };

    public static string Usage(UsageSignal usage, DateTimeOffset now) =>
        usage.LastUsed is { } last ? "Son kullanım " + Format.Ago(last, now) : "Kullanım bilgisi yok";
}

public sealed record KindBar(string Label, string SizeText, double Share, UnitKind Kind);

public sealed record TopUnit(string Name, string KindLabel, string SizeText, string UsageText);

public sealed record DriveChip(string? Root, string Label, string Detail, double Share);

public sealed partial class OverviewViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly DateTimeOffset _since = DateTimeOffset.Now;
    long? _rulesBytes;
    bool _rulesStale;
    int _estimateRun;
    long _silentBytes;
    long _moreBytes;
    int _moreCount;
    object _purgeToken = new();

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        FreeProgress = main.NewProgress();
        _weeklyCheck = main.Backend.WeeklyCheck;
        Purge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TwoStep.Target))
            {
                OnPropertyChanged(nameof(IsFreeNowArmed));
                OnPropertyChanged(nameof(FreeNowButtonText));
            }
        };
        main.Ticked += Purge.Elapse;
        Session.PropertyChanged += OnSession;
        Session.Scan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TaskProgressViewModel.IsRunning))
                return;
            Raise();
            if (!IsWaitingScan)
                _ = EstimateAsync();
        };
        Plan();
        Counters();
        if (Session.HasSnapshot && !IsWaitingScan)
            _ = EstimateAsync();
    }

    public SessionState Session => _main.Session;
    public TaskProgressViewModel Progress => Session.Scan;

    public ObservableCollection<KindBar> Bars { get; } = [];
    public ObservableCollection<TopUnit> Top { get; } = [];
    public ObservableCollection<DriveChip> Drives { get; } = [];

    public bool HasDrives => Drives.Count > 0;

    [ObservableProperty]
    private DriveChip? _selectedDrive;

    bool _syncing;

    partial void OnSelectedDriveChanged(DriveChip? value)
    {
        if (!_syncing)
            Session.SelectedDrive = value?.Root;
    }

    void Chips()
    {
        var volumes = Session.Snapshot?.Volumes ?? [];
        var list = new List<DriveChip>();
        if (volumes.Count > 1)
        {
            var total = volumes.Sum(v => v.TotalBytes);
            var free = volumes.Sum(v => v.FreeBytes);
            list.Add(new DriveChip(null, "Tümü", Space(free, total), total <= 0 ? 0 : Math.Clamp((double)(total - free) / total, 0, 1)));
        }
        foreach (var v in volumes)
            list.Add(new DriveChip(volumes.Count > 1 ? v.Root : null, v.Label.Length > 0 ? $"{v.Letter} {v.Label}" : v.Letter, Space(v.FreeBytes, v.TotalBytes), v.UsedShare));
        _syncing = true;
        try
        {
            Drives.Clear();
            foreach (var chip in list)
                Drives.Add(chip);
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(HasDrives));
        SyncSelection();
    }

    static string Space(long free, long total) => $"{Format.Bytes(free)} boş / {Format.Bytes(total)}";

    void SyncSelection()
    {
        var root = Session.SelectedDrive;
        var chip = Drives.FirstOrDefault(d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase)) ?? Drives.FirstOrDefault();
        _syncing = true;
        try
        {
            SelectedDrive = chip;
        }
        finally
        {
            _syncing = false;
        }
    }

    public bool ScanRemovable
    {
        get => _main.Backend.ScanRemovable;
        set
        {
            if (_main.Backend.ScanRemovable == value)
                return;
            _main.Backend.ScanRemovable = value;
            OnPropertyChanged();
            if (Session.HasSnapshot && CanScan())
                _ = Run(ScanMode.Refresh);
        }
    }

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

    public TaskProgressViewModel FreeProgress { get; }
    public TwoStep Purge { get; } = new();
    public ObservableCollection<KindBar> Compare { get; } = [];

    [ObservableProperty]
    private string _monthText = "";

    [ObservableProperty]
    private string _lastCleanupText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompare))]
    private bool _hasLastCleanup;

    public bool HasCompare => HasLastCleanup && Compare.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLowDrive), nameof(LowDriveText))]
    private FreeNowOffer? _freeNowOffer;

    public bool HasLowDrive => FreeNowOffer is not null;
    public string LowDriveText => FreeNowOffer is { } o
        ? $"{o.Drive.Letter} sürücüsünde yer azaldı: disk %{o.Drive.UsedPercent} dolu, karantinada bu sürücüden {Format.Bytes(o.Bytes)} bekliyor."
        : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFreeNow), nameof(CounterText))]
    [NotifyCanExecuteChangedFor(nameof(FreeNowCommand))]
    private HeldSpace? _held;

    public bool HasFreeNow => Held is not null;
    public bool IsFreeNowArmed => Purge.IsArmed;
    public string FreeNowButtonText => IsFreeNowArmed ? TwoStep.ArmedText : "Şimdi yer aç";
    public string FreeNowTip => "Karantinadakileri beklemeden kalıcı siler; geri alınamaz";

    public long NowFreedBytes => Session.Ledger.Entries.Where(e => e.At >= _since).Sum(e => Math.Max(0, e.Bytes));

    public string CounterText => $"Şimdi boşalan {Format.Bytes(NowFreedBytes)} · {HeldText()}";

    string HeldText()
    {
        if (Session.Quarantine is null)
            return "Karantina okunuyor";
        if (Held is not { } held)
            return "Karantina boş";
        var autoPurge = _main.Quarantine is { } q ? q.AutoPurge : true;
        var due = !autoPurge ? "siz silene dek durur"
            : held.DaysLeft == 0 ? "birazdan boşalır"
            : $"{(held.SameDay ? "" : "ilki ")}{held.DaysLeft} gün sonra boşalır";
        return $"Karantinada {Format.Bytes(held.Bytes)} ({due})";
    }

    public const string WaitText = "Tarama bitince açılır";
    public const string SafeEmptyText = "Güvenle silinecek bir şey kalmadı";

    public bool IsWaitingScan => IsScanning || Session.IsRestoring;
    public bool IsSafeKnown => _rulesBytes is not null;
    public long SafeBytes => _silentBytes + (_rulesBytes ?? 0);
    public bool ShowSafe => HasSnapshot || IsScanning;
    public bool ScanIsPrimary => !ShowSafe;
    public bool IsSafeEmpty => HasSnapshot && !IsWaitingScan && IsSafeKnown && SafeBytes <= 0;
    public bool ShowSafeButton => ShowSafe && !IsSafeEmpty;
    public string SafeText => IsSafeKnown && !IsWaitingScan ? $"Güvenle silinebilir: {Format.Bytes(SafeBytes)} — Temizle" : "Güvenle temizle";
    public string SafeReason => IsWaitingScan ? WaitText : !IsSafeKnown ? "Ölçülüyor" : "";
    public bool HasSafeReason => SafeReason.Length > 0;
    public string SafeTip => IsWaitingScan ? WaitText : "Önbellek, geçici dosyalar ve kendiliğinden yeniden oluşan dosyalar sorulmadan silinir; kişisel dosyalara dokunulmaz";
    public string MoreText => $"Daha fazla yer: {Format.Bytes(_moreBytes)}, {Format.Count(_moreCount)} karar →";
    public bool HasMore => HasSnapshot && _moreCount > 0;
    public string MoreTip => IsWaitingScan ? WaitText : "Büyük ve uzun süredir açılmamış öğeleri tek tek sorar";
    public string AutoTip => IsWaitingScan ? WaitText : "Güvenli artıkları sormadan siler, büyük ve eski öğeleri tek tek sorar";

    [ObservableProperty]
    private bool _weeklyCheck;

    public string WeeklyCheckTip => "Haftada bir boş alanı ölçer; disk dolmak üzereyse Windows bildirimi gösterir. Hiçbir dosyayı silmez.";

    partial void OnWeeklyCheckChanged(bool value) => _ = SaveWeeklyCheck(value);

    async Task SaveWeeklyCheck(bool value)
    {
        var result = await _main.Backend.SetWeeklyCheckAsync(value);
        if (result.Error is { } error)
            _main.Fail("Haftalık disk kontrolü ayarlanamadı: " + error);
        else
            _main.Notify(value ? "Haftalık disk kontrolü açıldı" : "Haftalık disk kontrolü kapatıldı");
    }

    public bool HasSnapshot => Session.HasSnapshot;
    public bool IsRefreshing => Session.IsRefreshing;
    public bool IsScanning => Session.Scan.IsRunning;
    public bool HasError => Session.ScanError is not null && !Session.HasSnapshot && !IsScanning;
    public string ErrorText => Session.ScanError ?? "";
    public bool IsRestoring => Session.IsRestoring && !Session.HasSnapshot;
    public bool IsEmpty => !Session.HasSnapshot && !IsScanning && !Session.IsRestoring && Session.ScanError is null;
    public bool ShowContent => Session.HasSnapshot;
    public string PrimaryText => HasError ? "Yeniden dene" : Session.HasSnapshot || Session.IsRestoring ? "Yenile" : "Taramayı başlat";

    public Availability FastScanState => _main.Backend.FastScanAvailability();
    public bool FastScanEnabled => FastScanState.Enabled && !IsScanning && !Session.IsRestoring;
    const string RestoringTip = "Önceki tarama okunuyor; bitince tarama kendiliğinden başlar";
    public string FastScanTip => Session.IsRestoring ? RestoringTip : FastScanState.Reason;
    public string ScanTip => Session.IsRestoring ? RestoringTip
        : Session.HasSnapshot ? "Son taramadan bu yana değişenleri okur; tam taramadan çok daha kısa sürer"
        : "Sabit sürücülerin hepsi yönetici izni istemeden taranır";
    public string RescanTip => Session.IsRestoring ? RestoringTip : "Sürücüleri baştan tarar; yenileme şüpheli görünürse kullanın";

    void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionState.Snapshot):
                Chips();
                Rebuild();
                Plan();
                if (!IsWaitingScan)
                    _ = EstimateAsync();
                break;
            case nameof(SessionState.IsRestoring):
                if (!IsWaitingScan && (_rulesStale || _rulesBytes is null))
                    _ = EstimateAsync();
                break;
            case nameof(SessionState.SelectedDrive):
                SyncSelection();
                Rebuild();
                break;
            case nameof(SessionState.Quarantine):
                Counters();
                break;
            case nameof(SessionState.Ledger):
                Counters();
                _rulesStale = true;
                if (IsActive)
                    _ = EstimateAsync();
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
        OnPropertyChanged(nameof(IsRestoring));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(FastScanEnabled));
        OnPropertyChanged(nameof(FastScanTip));
        OnPropertyChanged(nameof(ScanTip));
        OnPropertyChanged(nameof(RescanTip));
        StartScanCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
        FastScanCommand.NotifyCanExecuteChanged();
        RaiseSafe();
    }

    void RaiseSafe()
    {
        OnPropertyChanged(nameof(IsWaitingScan));
        OnPropertyChanged(nameof(IsSafeKnown));
        OnPropertyChanged(nameof(SafeBytes));
        OnPropertyChanged(nameof(ShowSafe));
        OnPropertyChanged(nameof(ScanIsPrimary));
        OnPropertyChanged(nameof(IsSafeEmpty));
        OnPropertyChanged(nameof(ShowSafeButton));
        OnPropertyChanged(nameof(SafeText));
        OnPropertyChanged(nameof(SafeReason));
        OnPropertyChanged(nameof(HasSafeReason));
        OnPropertyChanged(nameof(SafeTip));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreTip));
        OnPropertyChanged(nameof(AutoTip));
        SafeCleanCommand.NotifyCanExecuteChanged();
        MoreSpaceCommand.NotifyCanExecuteChanged();
        AutoCleanCommand.NotifyCanExecuteChanged();
    }

    void Plan()
    {
        var units = Session.Snapshot?.Units ?? [];
        _silentBytes = units.Where(TourViewModel.Silent).Sum(u => Math.Max(0, u.SizeBytes));
        var picked = TourViewModel.Pick(units, DateTimeOffset.Now);
        _moreBytes = picked.Sum(u => Math.Max(0, u.SizeBytes));
        _moreCount = picked.Count;
        RaiseSafe();
    }

    public async Task EstimateAsync()
    {
        if (!Session.HasSnapshot || IsWaitingScan)
        {
            _rulesStale = true;
            return;
        }
        var run = ++_estimateRun;
        long bytes;
        try
        {
            bytes = await CleanupViewModel.SafeBytesAsync(_main.Backend, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            bytes = 0;
        }
        if (run != _estimateRun)
            return;
        _rulesBytes = bytes;
        _rulesStale = false;
        RaiseSafe();
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
        (string Root, ScanResult? Result) drive = Session.SelectedDrive is { } d && snapshot.For(d) is { } picked ? (d, picked) : ("", null);
        IReadOnlyList<Unit> units = drive.Result is null
            ? snapshot.Units
            : [.. snapshot.Units.Where(u => u.Drive.Equals(drive.Root, StringComparison.OrdinalIgnoreCase))];
        var multi = snapshot.Results.Count > 1;
        var total = units.Sum(u => u.SizeBytes);
        HeroText = Format.Bytes(total);
        UnitCountText = $"{Format.Count(units.Count)} birim";
        var now = DateTimeOffset.Now;
        LastScanText = $"Son tarama {Format.Ago(snapshot.FinishedAt, now)}, {snapshot.FinishedAt.ToLocalTime():HH:mm}";
        ScannedText = $"{Format.Count(drive.Result?.Files ?? snapshot.Files)} dosya tarandı";
        var groups = units.GroupBy(u => Group(u.Kind)).Select(g => (Kind: g.Key, Bytes: g.Sum(u => u.SizeBytes))).OrderByDescending(g => g.Bytes).ToList();
        var max = groups.Count == 0 ? 1 : Math.Max(1, groups[0].Bytes);
        foreach (var g in groups)
            Bars.Add(new KindBar(KindText.Label(g.Kind), Format.Bytes(g.Bytes), (double)g.Bytes / max, g.Kind));
        foreach (var u in units.OrderByDescending(u => u.Score).ThenByDescending(u => u.SizeBytes).Take(5))
            Top.Add(new TopUnit(u.Name, multi && drive.Result is null && u.Drive.Length > 0 ? $"{KindText.Label(u.Kind)} · {u.Drive.TrimEnd('\\')}" : KindText.Label(u.Kind), Format.Bytes(u.SizeBytes), KindText.Usage(u.Usage, now)));
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
        History();
        CheckSpace();
        OnPropertyChanged(nameof(NowFreedBytes));
        OnPropertyChanged(nameof(CounterText));
    }

    void History()
    {
        var ledger = Session.Ledger;
        var now = DateTimeOffset.Now;
        var month = ledger.FreedInMonth(now);
        MonthText = month > 0 ? $"Bu ay {Format.Bytes(month)} açtınız" : "Bu ay henüz yer açılmadı";
        Compare.Clear();
        if (ledger.Last is not { } last)
        {
            LastCleanupText = "";
            HasLastCleanup = false;
            OnPropertyChanged(nameof(HasCompare));
            return;
        }
        var where = last.Root is { Length: > 0 } root ? $" · {root.TrimEnd('\\')}" : "";
        LastCleanupText = $"Son temizlik {Format.Ago(last.At, now)}: {Format.Bytes(last.Bytes)} açıldı{where}";
        if (last.HasDisk)
        {
            Compare.Add(new KindBar("Önce", $"%{(int)Math.Floor(last.UsedBefore * 100)} dolu", last.UsedBefore, UnitKind.Folder));
            Compare.Add(new KindBar("Sonra", $"%{(int)Math.Floor(last.UsedAfter * 100)} dolu", last.UsedAfter, UnitKind.Game));
        }
        HasLastCleanup = true;
        OnPropertyChanged(nameof(HasCompare));
    }

    void CheckSpace()
    {
        var entries = Session.Quarantine?.Entries ?? [];
        FreeNowOffer = entries.Count == 0 ? null : DiskCheck.FreeNow(_main.Backend.Drives(), entries);
        var held = DiskCheck.Held(entries, DateTime.UtcNow);
        if (held is null ? Held is not null : !held.SameAs(Held))
        {
            _purgeToken = new object();
            Purge.Reset();
        }
        Held = held;
    }

    bool CanFreeNow() => Held is not null && !FreeProgress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanFreeNow))]
    private async Task FreeNow()
    {
        if (Held is not { } held || !Purge.Press(_purgeToken))
            return;
        var results = new List<(HeldRoot Root, WorkerResponse Response)>();
        try
        {
            await FreeProgress.RunAsync("Karantina kalıcı siliniyor", async (p, ct) =>
            {
                foreach (var root in held.Roots)
                    results.Add((root, await _main.Backend.SendAsync(new WorkerRequest
                    {
                        Op = Ops.Purge,
                        UserApproved = true,
                        Items = [.. root.Ids],
                    }, p, ct)));
                return true;
            }, cancellable: false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("İşlem yapılamadı: " + e.Message);
        }
        finally
        {
            FreeNowCommand.NotifyCanExecuteChanged();
        }
        long freed = 0;
        var dryRun = false;
        var errors = new List<string>();
        foreach (var (root, response) in results)
        {
            if (response.DryRun)
            {
                dryRun = true;
                continue;
            }
            if (response.FreedBytes > 0)
                Session.AddFreed(response.FreedBytes, root.Root);
            freed += Math.Max(0, response.FreedBytes);
            if (!response.Ok)
                errors.Add(response.Message);
        }
        if (dryRun)
            _main.Notify("Prova: hiçbir öğe silinmedi");
        else if (errors.Count > 0)
            _main.Fail("Silme tamamlanamadı: " + string.Join("; ", errors));
        else if (results.Count > 0)
            _main.Notify($"Karantina boşaltıldı, {Format.Bytes(freed)} açıldı");
        await Session.RefreshQuarantineAsync(_main);
    }

    protected override void OnNavigatedFrom() => Purge.Reset();

    protected override void OnNavigatedTo()
    {
        Counters();
        Raise();
        if (_rulesStale || _rulesBytes is null)
            _ = EstimateAsync();
        _ = Session.RefreshQuarantineAsync(_main);
    }

    bool CanScan() => !IsScanning && !Session.IsRestoring;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task StartScan() => Run(Session.HasSnapshot ? ScanMode.Refresh : ScanMode.Full);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task Rescan() => Run(ScanMode.Full);

    async Task Run(ScanMode mode)
    {
        var task = Session.RunScanAsync(_main, mode);
        Raise();
        await task;
        Raise();
    }

    bool CanFastScan() => FastScanEnabled;

    [RelayCommand(CanExecute = nameof(CanFastScan))]
    private Task FastScan() => Run(ScanMode.Fast);

    [RelayCommand]
    private void OpenOffers() => _main.GoTo(_main.Offers);

    bool CanTour() => HasSnapshot && !IsWaitingScan;

    bool CanSafeClean() => CanTour() && IsSafeKnown && SafeBytes > 0;

    bool CanMoreSpace() => CanTour() && _moreCount > 0;

    [RelayCommand(CanExecute = nameof(CanSafeClean))]
    private Task SafeClean() => _main.StartTourAsync(TourMode.Safe);

    [RelayCommand(CanExecute = nameof(CanMoreSpace))]
    private Task MoreSpace() => _main.StartTourAsync(TourMode.Ask);

    [RelayCommand(CanExecute = nameof(CanTour))]
    private Task AutoClean() => _main.StartTourAsync();
}
