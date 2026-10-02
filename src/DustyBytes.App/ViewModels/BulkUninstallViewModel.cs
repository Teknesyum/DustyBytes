using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class BulkItem(ProgramRow row) : ObservableObject
{
    public ProgramRow Row { get; } = row;
    public string Name => Row.Name;
    public string SizeText => Row.SizeText;
    public bool IsShared => Row.IsShared;
    public string SharedNote => Row.SharedNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private StepState _state;

    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _awaitingChoice;

    public UninstallPlan Plan => Row.Plan;
    public string Badge => Plan.Badge;
    public bool HasBadge => Badge.Length > 0;
    public long Freed { get; set; }
    public int LeftoversRemoved { get; set; }
    public int LeftoversKept { get; set; }
    public bool RebootRequired { get; set; }

    public string StateText => AwaitingChoice ? "Bekliyor…" : State switch
    {
        StepState.Running => "Sürüyor",
        StepState.Done => "Kaldırıldı",
        StepState.Failed => "Olmadı",
        StepState.Skipped => "Atlandı",
        _ => "Sırada",
    };
}

public sealed partial class BulkUninstallViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly ProgramsViewModel _owner;
    bool _started;
    bool _restoreDone;
    bool _noRestorePoint;

    public BulkUninstallViewModel(MainViewModel main, ProgramsViewModel owner, IReadOnlyList<ProgramRow> rows)
    {
        _main = main;
        _owner = owner;
        Progress = main.NewProgress();
        var items = rows.Select(r => new BulkItem(r)).ToList();
        Items = [.. items.Where(i => i.Plan.IsSilent), .. items.Where(i => !i.Plan.IsSilent)];
    }

    public static readonly TimeSpan SilentPatience = TimeSpan.FromMinutes(5);

    TaskCompletionSource<bool>? _choice;

    public TaskProgressViewModel Progress { get; }
    public ObservableCollection<BulkItem> Items { get; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public string Heading => $"{Format.Count(Items.Count)} program sırayla kaldırılıyor";
    public int SilentCount => Items.Count(i => i.Plan.IsSilent);
    public int WindowCount => Items.Count - SilentCount;
    public string PlanText => WindowCount == 0
        ? $"{Format.Count(SilentCount)} program sessiz kaldırılır; pencere açılmaz."
        : SilentCount == 0
            ? $"{Format.Count(WindowCount)} programın kendi kaldırıcı penceresi sırayla açılır."
            : $"{Format.Count(SilentCount)} program önce sessiz kaldırılır, sonra {Format.Count(WindowCount)} programın kaldırıcı penceresi sırayla açılır.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RunVisibleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipWaitingCommand))]
    private bool _choiceVisible;

    [ObservableProperty]
    private string _choiceText = "";

    [ObservableProperty]
    private string _reportText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WizardText))]
    private string _wizardName = "";

    [ObservableProperty]
    private bool _wizardVisible;

    public string WizardText => $"{WizardName} sessiz kaldırılamadı; kendi kaldırma penceresi açıldı. Pencereyi siz bitirin, kapandığında sıra kendiliğinden sürer.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _finished;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _stopRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _current = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _freedText = "";

    public bool IsRunning => !Finished;

    public string StatusText =>
        Finished ? "Sıra bitti"
        : ChoiceVisible ? "Bekliyor…: sessiz kaldırılamayan programlar için seçiminiz gerekiyor"
        : StopRequested ? $"İptal istendi; {Current} bitince sıra durur"
        : Current.Length > 0 ? $"Şimdi: {Current}"
        : "Sıra hazırlanıyor";

    protected override void OnNavigatedTo()
    {
        if (!_started)
        {
            _started = true;
            Completion = RunAsync();
        }
    }

    public async Task RunAsync()
    {
        var silentFlags = new[] { UninstallHandlers.SilentOnly, UninstallHandlers.Patience(SilentPatience) };
        foreach (var item in Items.Where(i => i.Plan.IsSilent).ToList())
            await Step(item, silentFlags);

        var chosen = await DecideAsync();

        foreach (var item in Items.Where(i => !i.Plan.IsSilent).ToList())
            await Step(item, []);
        foreach (var item in chosen)
            await Step(item, [UninstallHandlers.VisibleOnly]);

        WizardVisible = false;
        Current = "";
        Finish();
    }

    async Task Step(BulkItem item, IReadOnlyList<string> extra)
    {
        if (StopRequested)
        {
            item.AwaitingChoice = false;
            item.State = StepState.Skipped;
            item.Detail = "İptal edildi; bu programa dokunulmadı";
            return;
        }
        if (!item.Row.CanUninstall || item.Row.UninstallerMissing)
        {
            item.State = StepState.Failed;
            item.Detail = "Kaldırıcısı yok ya da bozuk; programı tek başına açıp zorla kaldırmayı deneyin";
            return;
        }
        item.AwaitingChoice = false;
        Current = item.Name;
        await RunOneAsync(item, extra);
    }

    async Task<List<BulkItem>> DecideAsync()
    {
        var waiting = Items.Where(i => i.AwaitingChoice).ToList();
        if (waiting.Count == 0)
            return [];
        if (!StopRequested)
        {
            Current = "";
            ChoiceText = $"{Format.Count(waiting.Count)} program sessiz kaldırılamadı ya da süresinde bitmedi: "
                + string.Join(", ", waiting.Take(5).Select(i => i.Name)) + (waiting.Count > 5 ? $" ve {Format.Count(waiting.Count - 5)} tane daha" : "")
                + ". Görünür çalıştırırsanız kaldırıcıların kendi pencereleri sırayla açılır ve onları siz bitirirsiniz.";
            _choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ChoiceVisible = true;
            var run = await _choice.Task;
            ChoiceVisible = false;
            if (run && !StopRequested)
                return waiting;
        }
        foreach (var item in waiting)
        {
            item.AwaitingChoice = false;
            item.State = StepState.Failed;
            item.Detail = StopRequested ? "İptal edildi; sessiz kaldırma tamamlanmadı" : $"{item.Detail}; görünür çalıştırılmadı";
        }
        return [];
    }

    async Task RunOneAsync(BulkItem item, IReadOnlyList<string> extra)
    {
        item.State = StepState.Running;
        item.Detail = "";
        List<string> flags = [UninstallHandlers.AutoClean, .. extra];
        if (_restoreDone || _noRestorePoint)
            flags.Add(UninstallHandlers.SkipRestorePoint);
        while (true)
        {
            WorkerResponse response;
            try
            {
                response = await Progress.RunAsync($"{item.Name} kaldırılıyor", (p, ct) => _main.Backend.SendAsync(new WorkerRequest
                {
                    Op = Ops.Uninstall,
                    Target = item.Row.Info.Program.Id,
                    UserApproved = true,
                    Items = [.. flags],
                }, new Progress<TaskStep>(s =>
                {
                    p.Report(s);
                    if (s.Step == Uninstaller.VisibleStep)
                    {
                        WizardName = item.Name;
                        WizardVisible = s.Percent < 100;
                    }
                }), ct), cancellable: false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
            {
                WizardVisible = false;
                item.State = StepState.Failed;
                item.Detail = "Kaldırma başlatılamadı: " + e.Message;
                return;
            }
            WizardVisible = false;

            var restore = response.Items.FirstOrDefault(i => i.Path == "restore-point");
            if (!response.Ok && restore is { Ok: false } && response.Items.Count == 1 && !flags.Contains(UninstallHandlers.ContinueWithoutRestorePoint))
            {
                if (await _main.ConfirmAsync(
                        "Geri yükleme noktası oluşturulamadı",
                        restore.Message + ". Noktasız devam edilirse bu sıradaki kaldırmalar geri alınamaz.",
                        "Noktasız devam et"))
                {
                    _noRestorePoint = true;
                    flags.Add(UninstallHandlers.ContinueWithoutRestorePoint);
                    continue;
                }
                StopRequested = true;
                item.State = StepState.Skipped;
                item.Detail = "Geri yükleme noktası olmadan devam edilmedi";
                return;
            }
            if (restore is { Ok: true })
                _restoreDone = true;
            Apply(item, response);
            return;
        }
    }

    void Apply(BulkItem item, WorkerResponse response)
    {
        var after = response.Payload is { Length: > 0 } payload ? JsonSerializer.Deserialize(payload, UninstallJson.Default.LeftoverSnapshot) : null;
        var auto = response.Items.FirstOrDefault(i => i.Path == UninstallHandlers.AutoClean);
        if (response.DryRun)
        {
            item.State = StepState.Skipped;
            item.Detail = "Prova kipi: kaldırıcı çalışmadı";
            return;
        }
        if (response.Items.Any(i => i.Path == UninstallHandlers.SilentGaveUp) && after is not { ProgramStillInstalled: false })
        {
            item.State = StepState.Waiting;
            item.AwaitingChoice = true;
            item.Detail = response.Message;
            return;
        }
        if (response.Ok && after is { ProgramStillInstalled: false })
        {
            item.State = StepState.Done;
            item.Freed = FreedBytes(item.Row, after);
            item.LeftoversRemoved = after.AutoRemoved.Count(i => i.Ok);
            item.LeftoversKept = after.Candidates.Count(c => c.Tier != ConfidenceTier.Low);
            item.RebootRequired = response.Message.Contains(Uninstaller.RebootNote, StringComparison.Ordinal);
            item.Detail = auto?.Message is { Length: > 0 } m ? $"{response.Message}. {m}" : response.Message;
            _owner.Removed(item.Row);
            return;
        }
        item.State = StepState.Failed;
        item.Detail = after is { ProgramStillInstalled: true }
            ? "Program hâlâ kurulu görünüyor; kalıntıya dokunulmadı. Tek başına açıp zorla kaldırmayı deneyebilirsiniz"
            : response.Message;
    }

    public static long FreedBytes(ProgramRow row, LeftoverSnapshot after)
    {
        var location = row.Info.Program.InstallLocation is { Length: > 0 } l ? l.TrimEnd('\\') + "\\" : null;
        var outside = after.AutoRemoved
            .Where(i => i.Ok && (location is null || !i.Target.StartsWith(location, StringComparison.OrdinalIgnoreCase) && !i.Target.Equals(location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            .Sum(i => i.Bytes);
        return row.ShownBytes + outside;
    }

    void Finish()
    {
        var removed = Items.Count(i => i.State == StepState.Done);
        var failed = Items.Count(i => i.State == StepState.Failed);
        var skipped = Items.Count(i => i.State == StepState.Skipped);
        var freed = Items.Sum(i => i.Freed);
        Summary = $"{Format.Count(removed)} program kaldırıldı, {Format.Count(failed)} program kaldırılamadı"
            + (skipped > 0 ? $", {Format.Count(skipped)} program atlandı" : "");
        FreedText = removed > 0
            ? $"Açılan yer yaklaşık {Format.Bytes(freed)}. Temizlenen kalıntılar 7 gün karantinada kalır."
            : "Yer açılmadı.";
        var cleaned = Items.Sum(i => i.LeftoversRemoved);
        var kept = Items.Sum(i => i.LeftoversKept);
        var reboot = Items.Count(i => i.RebootRequired);
        var report = $"Kalıntı: {Format.Count(cleaned)} kalıntı temizlendi, {Format.Count(kept)} kalıntı incelemenizi bekliyor.";
        if (reboot > 0)
            report += $" {Format.Count(reboot)} program için yeniden başlatma gerekiyor.";
        ReportText = report;
        Finished = true;
        if (removed > 0)
            _ = _main.Session.RefreshQuarantineAsync(_main);
        if (failed == 0 && removed > 0)
            _main.Notify(Summary);
        else if (failed > 0)
            _main.Fail(Summary);
    }

    bool CanCancel() => !StopRequested && !Finished;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        StopRequested = true;
        _choice?.TrySetResult(false);
    }

    bool CanChoose() => ChoiceVisible;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void RunVisible() => _choice?.TrySetResult(true);

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void SkipWaiting() => _choice?.TrySetResult(false);

    bool CanBack() => Finished;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => _main.Navigation.Pop();
}
