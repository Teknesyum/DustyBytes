using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum StepState
{
    Waiting,
    Running,
    Done,
    Failed,
    Skipped,
}

public sealed partial class UninstallStep(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    private StepState _state;

    [ObservableProperty]
    private string _detail = "";

    public bool IsDone => State == StepState.Done;
    public bool IsFailed => State == StepState.Failed;

    public string StateText => State switch
    {
        StepState.Running => "Sürüyor",
        StepState.Done => "Tamam",
        StepState.Failed => "Olmadı",
        StepState.Skipped => "Atlandı",
        _ => "Bekliyor",
    };
}

public sealed partial class LeftoverRow(LeftoverCandidate candidate, Action changed, bool check = true) : ObservableObject
{
    public LeftoverCandidate Candidate { get; } = candidate;
    public string Target => Candidate.Target;
    public string Reason => Candidate.Reason;
    public string SizeText => Candidate.Bytes > 0 ? Format.Bytes(Candidate.Bytes) : "";
    public ConfidenceTier Tier => Candidate.Tier;

    public string KindLabel => Candidate.Kind switch
    {
        LeftoverKind.Folder => "Klasör",
        LeftoverKind.File => "Dosya",
        LeftoverKind.RegistryKey => "Kayıt anahtarı",
        LeftoverKind.RegistryValue => "Kayıt değeri",
        LeftoverKind.Service => "Hizmet",
        LeftoverKind.ScheduledTask => "Zamanlanmış görev",
        LeftoverKind.StartupEntry => "Başlangıç kaydı",
        LeftoverKind.Shortcut => "Kısayol",
        LeftoverKind.FileAssociation => "Dosya ilişkisi",
        _ => "Güvenlik duvarı kuralı",
    };

    public string TierText => Candidate.Tier switch
    {
        ConfidenceTier.High => "Yüksek güven",
        ConfidenceTier.Medium => "Orta güven",
        _ => "Düşük güven",
    };

    [ObservableProperty]
    private bool _isChecked = check && candidate.Tier == ConfidenceTier.High;

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed partial class UninstallViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly ProgramsViewModel _owner;
    readonly ProgramRow _row;
    readonly List<LeftoverRow> _hidden = [];
    LeftoverSnapshot? _snapshot;
    bool _started;
    bool _forced;

    public UninstallViewModel(MainViewModel main, ProgramsViewModel owner, ProgramRow row)
    {
        _main = main;
        _owner = owner;
        _row = row;
        Progress = main.NewProgress();
        Steps =
        [
            new UninstallStep("restore-point", "Geri yükleme noktası oluşturma"),
            new UninstallStep("snapshot", "Önceki durumu kaydetme"),
            new UninstallStep("vendor", "Programın kendi kaldırıcısı"),
            new UninstallStep("diff", "Kalan izleri karşılaştırma"),
            new UninstallStep(UninstallHandlers.AutoClean, "Kesin kalıntıları temizleme"),
        ];
    }

    public TaskProgressViewModel Progress { get; }
    public string Name => _row.Name;
    public string Publisher => _row.Publisher;
    public string SizeText => _row.SizeText;
    public ObservableCollection<UninstallStep> Steps { get; }
    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];
    public ObservableCollection<RemovalItem> Cleaned { get; } = [];

    [ObservableProperty]
    private bool _vendorVisible;

    public string VendorCard => Uninstaller.VisibleCard;

    [ObservableProperty]
    private bool _autoClean = true;

    [ObservableProperty]
    private bool _keepSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCleaned))]
    private string _cleanedText = "";

    public bool HasCleaned => Cleaned.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsAfter))]
    private bool _uninstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _vendorFailed;

    public bool ForceOffered => !IsDryRun && !_forced && _row.CanForceAfter(VendorFailed, Uninstalled);
    public bool ShowUninstallRun => IsPreview && !ForceOffered;
    public bool ShowRemoveLeftovers => IsAfter && !ForceOffered;
    public bool ShowBottomTip => IsAfter || ForceOffered;
    public string BottomTip => ForceOffered ? "Onay sorulur; kaldırıcı çalıştırılmaz, yalnız kesin izler karantinaya alınır" : RemoveTip;

    public string ForceReason => VendorFailed
        ? "Programın kendi kaldırıcısı işini bitiremedi; program hâlâ kurulu görünüyor."
        : "Programın kendi kaldırıcısı bulunamadı ya da bozuk; normal kaldırma çalışmaz.";

    public bool IsPreview => !Uninstalled;
    public bool IsAfter => Uninstalled;
    public bool IsBusy => Progress.IsRunning;
    public bool HasLeftovers => Leftovers.Count > 0;
    public bool HasError => Error is not null;
    public bool IsDryRun => _main.Backend.DryRun;

    public string LeftoverHint => HasCleaned
        ? "Kesin olanlar temizlendi. Bunlardan emin olunamadı; gerçekten bu programınsa işaretleyip silin."
        : "Yüksek güvenliler işaretli gelir; ortalar elle seçilir.";

    public string MoreText => $"Daha fazla göster ({_hidden.Count} düşük güvenli)";

    public string RemoveTip =>
        IsDryRun ? "Prova kipinde kaldırıcı çalışmaz; kalıntı silme kapalı"
        : _snapshot is not { IsDiff: true } ? "Kalıntılar kaldırıcı çalıştıktan sonra silinir"
        : _snapshot.ProgramStillInstalled ? "Program hâlâ kurulu; kalıntı silinmez"
        : Checked.Count == 0 ? "Silinecek kalıntı seçin"
        : "Seçilen kalıntılar yedeklenip kaldırılır";

    List<LeftoverRow> Checked => [.. Leftovers.Where(l => l.IsChecked)];

    protected override void OnNavigatedTo()
    {
        if (!_started)
        {
            _started = true;
            _ = PreviewAsync();
        }
    }

    async Task PreviewAsync()
    {
        Error = null;
        try
        {
            var snapshot = await Progress.RunAsync("Kalıntılar önceden taranıyor", (p, ct) => _main.Backend.PreviewLeftoversAsync(_row.Info.Program, p, ct));
            Show(snapshot);
            Summary = snapshot.Candidates.Count == 0
                ? "Kaldırıldıktan sonra geriye iz kalması beklenmiyor"
                : $"Kaldırıldıktan sonra {snapshot.Candidates.Count} olası iz denetlenecek";
        }
        catch (OperationCanceledException)
        {
            Error = "Önizleme iptal edildi";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = "Önizleme yapılamadı: " + e.Message;
        }
        Raise();
    }

    void Show(LeftoverSnapshot snapshot, bool check = true)
    {
        _snapshot = snapshot;
        Leftovers.Clear();
        _hidden.Clear();
        foreach (var c in snapshot.Candidates.OrderByDescending(c => c.Tier).ThenByDescending(c => c.Score))
        {
            var row = new LeftoverRow(c, Raise, check);
            if (c.Tier == ConfidenceTier.Low)
                _hidden.Add(row);
            else
                Leftovers.Add(row);
        }
        HasMore = _hidden.Count > 0;
        Notes = string.Join(Environment.NewLine, snapshot.Notes);
        OnPropertyChanged(nameof(MoreText));
        Raise();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasLeftovers));
        OnPropertyChanged(nameof(HasCleaned));
        OnPropertyChanged(nameof(LeftoverHint));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(RemoveTip));
        OnPropertyChanged(nameof(ForceOffered));
        OnPropertyChanged(nameof(ShowUninstallRun));
        OnPropertyChanged(nameof(ShowRemoveLeftovers));
        OnPropertyChanged(nameof(ShowBottomTip));
        OnPropertyChanged(nameof(BottomTip));
        OnPropertyChanged(nameof(ForceReason));
        UninstallCommand.NotifyCanExecuteChanged();
        RemoveLeftoversCommand.NotifyCanExecuteChanged();
        ForceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowMore()
    {
        foreach (var row in _hidden)
            Leftovers.Add(row);
        _hidden.Clear();
        HasMore = false;
        Raise();
    }

    bool CanUninstall() => !Uninstalled && !Progress.IsRunning && _row.CanUninstall && !ForceOffered;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task Uninstall()
    {
        var body = "Önce geri yükleme noktası oluşturulur, sonra programın kendi kaldırıcısı sessiz çalışır; sessiz olmazsa kendi penceresi açılır.";
        if (AutoClean)
            body += KeepSettings
                ? " Ardından kesin kalıntılar kayıt yedeği alınıp 7 gün karantinada tutularak temizlenir; ayar klasörleri korunur."
                : " Ardından kesin kalıntılar kayıt yedeği alınıp 7 gün karantinada tutularak temizlenir.";
        if (!await _main.ConfirmAsync($"{Name} kaldırılsın mı?", body + " Kaldırılan program yeniden kurulmadan geri gelmez.", "Programı kaldır"))
            return;
        await RunUninstallAsync(Flags());
    }

    public List<string> Flags()
    {
        var flags = new List<string>();
        if (AutoClean)
            flags.Add(UninstallHandlers.AutoClean);
        if (AutoClean && KeepSettings)
            flags.Add(UninstallHandlers.KeepSettings);
        return flags;
    }

    public async Task RunUninstallAsync(List<string> flags)
    {
        foreach (var step in Steps)
        {
            step.State = StepState.Waiting;
            step.Detail = "";
        }
        Steps[0].State = StepState.Running;
        WorkerResponse response;
        try
        {
            response = await Progress.RunAsync($"{Name} kaldırılıyor", (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = Ops.Uninstall,
                Target = _row.Info.Program.Id,
                UserApproved = true,
                Items = flags,
            }, new Progress<TaskStep>(s =>
            {
                p.Report(s);
                if (s.Step == Uninstaller.VisibleStep)
                    VendorVisible = s.Percent < 100;
            }), ct), cancellable: false);
            VendorVisible = false;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            VendorVisible = false;
            Steps[0].State = StepState.Failed;
            _main.Fail("Kaldırma başlatılamadı: " + e.Message);
            Raise();
            return;
        }

        foreach (var item in response.Items)
            if (Steps.FirstOrDefault(s => s.Key == item.Path) is { } step)
            {
                step.State = item.Ok ? StepState.Done : StepState.Failed;
                step.Detail = item.Message;
            }

        var restore = response.Items.FirstOrDefault(i => i.Path == "restore-point");
        if (!response.Ok && restore is { Ok: false } && response.Items.Count == 1 && !flags.Contains(UninstallHandlers.ContinueWithoutRestorePoint))
        {
            Raise();
            if (await _main.ConfirmAsync(
                    "Geri yükleme noktası oluşturulamadı",
                    restore.Message + ". Geri yükleme noktası olmadan devam edilirse kaldırma geri alınamaz.",
                    "Noktasız devam et"))
                await RunUninstallAsync([.. flags, UninstallHandlers.ContinueWithoutRestorePoint]);
            return;
        }

        if (response.Payload is { Length: > 0 } payload && JsonSerializer.Deserialize(payload, UninstallJson.Default.LeftoverSnapshot) is { } after)
        {
            Uninstalled = true;
            VendorFailed = !response.DryRun && (response.Items.FirstOrDefault(i => i.Path == "vendor") is { Ok: false } || after.ProgramStillInstalled);
            Steps[3].State = after.IsDiff ? StepState.Done : StepState.Skipped;
            Steps[3].Detail = after.IsDiff ? $"{after.Candidates.Count + after.AutoRemoved.Count(i => i.Ok)} kalıntı bulundu" : "Karşılaştırma yapılmadı";
            var auto = response.Items.FirstOrDefault(i => i.Path == UninstallHandlers.AutoClean);
            if (auto is null)
                Steps[4].State = StepState.Skipped;
            Cleaned.Clear();
            foreach (var item in after.AutoRemoved)
                Cleaned.Add(item);
            CleanedText = Cleaned.Count == 0 ? "" : auto?.Message ?? "";
            OnPropertyChanged(nameof(HasCleaned));
            Show(after, check: auto is null);
            Summary = response.DryRun
                ? "Prova kipi: kaldırıcı çalışmadı, liste önizlemedir"
                : after.ProgramStillInstalled ? "Program hâlâ kurulu görünüyor; kalıntılar silinmez"
                : auto is { Ok: true } && after.Candidates.Count == 0 ? response.Message + ". Geride iz kalmadı"
                : auto is not null && after.Candidates.Count > 0 ? response.Message + ". Emin olunamayan izler aşağıda, işaretsiz"
                : response.Message;
            if (Cleaned.Count > 0)
                _ = _main.Session.RefreshQuarantineAsync(_main);
            if (response.Ok)
                _main.Notify($"{Name}: {response.Message}");
            else
                _main.Fail($"{Name}: {response.Message}");
            if (response.Ok && !response.DryRun && !after.ProgramStillInstalled)
                _owner.Removed(_row);
        }
        else
        {
            _main.Fail($"{Name}: {response.Message}");
        }
        Raise();
    }

    bool CanForce() => ForceOffered && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanForce))]
    private async Task Force()
    {
        var body = "Programın kendi kaldırıcısı çalıştırılmaz. Önce geri yükleme noktası oluşturulur. "
            + "Kurulum klasörü ve iki bağımsız kanıtla bu programa bağlanan yüksek güvenli izler 7 gün karantinaya alınır; kayıt anahtarları önce .reg dosyasına yedeklenir. "
            + "Programın listedeki kaydı ancak dosyaları gittikten sonra silinir. "
            + (KeepSettings ? "Ayar klasörleri korunur. " : "")
            + "Emin olunamayan izlere dokunulmaz.";
        if (!await _main.ConfirmAsync($"{Name} zorla kaldırılsın mı?", body, "Zorla kaldır"))
            return;
        await RunForceAsync(KeepSettings ? [UninstallHandlers.KeepSettings] : []);
    }

    public async Task RunForceAsync(List<string> flags)
    {
        Steps.Clear();
        Steps.Add(new UninstallStep("restore-point", "Geri yükleme noktası oluşturma"));
        Steps.Add(new UninstallStep("snapshot", "İzleri bulma"));
        Steps.Add(new UninstallStep(ForceUninstall.FilesStep, "Kesin izleri karantinaya alma"));
        Steps.Add(new UninstallStep(ForceUninstall.EntryStep, "Program kaydını silme"));
        Steps[0].State = StepState.Running;
        Raise();
        WorkerResponse response;
        try
        {
            response = await Progress.RunAsync($"{Name} zorla kaldırılıyor", (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = Ops.ForceUninstall,
                Target = _row.Info.Program.Id,
                UserApproved = true,
                Items = flags,
            }, p, ct), cancellable: false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            Steps[0].State = StepState.Failed;
            _main.Fail("Zorla kaldırma başlatılamadı: " + e.Message);
            Raise();
            return;
        }

        foreach (var item in response.Items)
            if (Steps.FirstOrDefault(s => s.Key == item.Path) is { } step)
            {
                step.State = item.Ok ? StepState.Done : StepState.Failed;
                step.Detail = item.Message;
            }
        foreach (var step in Steps.Where(s => s.State is StepState.Waiting or StepState.Running))
            step.State = StepState.Skipped;

        var restore = response.Items.FirstOrDefault(i => i.Path == "restore-point");
        if (!response.Ok && restore is { Ok: false } && response.Items.Count == 1 && !flags.Contains(UninstallHandlers.ContinueWithoutRestorePoint))
        {
            Raise();
            if (await _main.ConfirmAsync(
                    "Geri yükleme noktası oluşturulamadı",
                    restore.Message + ". Geri yükleme noktası olmadan devam edilirse kaldırma geri alınamaz; karantina ve .reg yedeği yine de alınır.",
                    "Noktasız devam et"))
                await RunForceAsync([.. flags, UninstallHandlers.ContinueWithoutRestorePoint]);
            return;
        }

        if (response.Payload is { Length: > 0 } payload && JsonSerializer.Deserialize(payload, UninstallJson.Default.LeftoverSnapshot) is { } after)
        {
            _forced = true;
            Uninstalled = true;
            Cleaned.Clear();
            foreach (var item in after.AutoRemoved)
                Cleaned.Add(item);
            CleanedText = Cleaned.Count == 0 ? "" : response.Items.FirstOrDefault(i => i.Path == ForceUninstall.FilesStep)?.Message ?? "";
            OnPropertyChanged(nameof(HasCleaned));
            Show(after, check: false);
            Summary = response.DryRun
                ? "Prova kipi: hiçbir şey taşınmadı, liste önizlemedir"
                : response.Message;
            if (Cleaned.Count > 0)
                _ = _main.Session.RefreshQuarantineAsync(_main);
            if (response.Ok)
                _main.Notify($"{Name}: {response.Message}");
            else
                _main.Fail($"{Name}: {response.Message}");
            if (response.Ok && !response.DryRun && !after.ProgramStillInstalled)
                _owner.Removed(_row);
        }
        else
        {
            Error = response.Message;
            _main.Fail($"{Name}: {response.Message}");
        }
        Raise();
    }

    bool CanRemove() => !IsDryRun && !Progress.IsRunning && _snapshot is { IsDiff: true, ProgramStillInstalled: false } && Checked.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveLeftovers()
    {
        var chosen = Checked;
        if (_snapshot is null || chosen.Count == 0)
            return;
        try
        {
            var response = await Progress.RunAsync("Kalıntılar kaldırılıyor", (p, ct) => _main.Backend.SendAsync(new WorkerRequest
            {
                Op = Ops.RemoveLeftovers,
                UnitId = _snapshot.Id,
                UserApproved = true,
                Items = [.. chosen.Select(c => c.Candidate.Id)],
            }, p, ct), cancellable: false);
            if (response.Ok)
            {
                foreach (var row in chosen)
                    Leftovers.Remove(row);
                _main.Notify(response.Message);
            }
            else
            {
                _main.Fail(response.Message);
            }
            _ = _main.Session.RefreshQuarantineAsync(_main);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Kalıntılar silinemedi: " + e.Message);
        }
        Raise();
    }

    [RelayCommand]
    private void Back() => _main.Navigation.Pop();
}
