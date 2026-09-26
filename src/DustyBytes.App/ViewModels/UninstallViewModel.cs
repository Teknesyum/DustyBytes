using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public sealed partial class LeftoverRow(LeftoverCandidate candidate, Action changed) : ObservableObject
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
    private bool _isChecked = candidate.Tier == ConfidenceTier.High;

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
        ];
    }

    public TaskProgressViewModel Progress { get; }
    public string Name => _row.Name;
    public string Publisher => _row.Publisher;
    public string SizeText => _row.SizeText;
    public ObservableCollection<UninstallStep> Steps { get; }
    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];

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

    public bool IsPreview => !Uninstalled;
    public bool IsAfter => Uninstalled;
    public bool IsBusy => Progress.IsRunning;
    public bool HasLeftovers => Leftovers.Count > 0;
    public bool HasError => Error is not null;
    public bool IsDryRun => _main.Backend.DryRun;

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

    void Show(LeftoverSnapshot snapshot)
    {
        _snapshot = snapshot;
        Leftovers.Clear();
        _hidden.Clear();
        foreach (var c in snapshot.Candidates.OrderByDescending(c => c.Tier).ThenByDescending(c => c.Score))
        {
            var row = new LeftoverRow(c, Raise);
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
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(RemoveTip));
        UninstallCommand.NotifyCanExecuteChanged();
        RemoveLeftoversCommand.NotifyCanExecuteChanged();
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

    bool CanUninstall() => !Uninstalled && !Progress.IsRunning && _row.CanUninstall;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task Uninstall()
    {
        if (!await _main.ConfirmAsync(
                $"{Name} kaldırılsın mı?",
                "Önce geri yükleme noktası oluşturulur, sonra programın kendi kaldırıcısı çalışır. Kaldırılan program yeniden kurulmadan geri gelmez.",
                "Programı kaldır"))
            return;
        await RunUninstallAsync([]);
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
            }, p, ct), cancellable: false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
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
                await RunUninstallAsync([UninstallHandlers.ContinueWithoutRestorePoint]);
            return;
        }

        if (response.Payload is { Length: > 0 } payload && JsonSerializer.Deserialize(payload, UninstallJson.Default.LeftoverSnapshot) is { } after)
        {
            Uninstalled = true;
            Steps[3].State = after.IsDiff ? StepState.Done : StepState.Skipped;
            Steps[3].Detail = after.IsDiff ? $"{after.Candidates.Count} kalıntı bulundu" : "Karşılaştırma yapılmadı";
            Show(after);
            Summary = response.DryRun
                ? "Prova kipi: kaldırıcı çalışmadı, liste önizlemedir"
                : after.ProgramStillInstalled ? "Program hâlâ kurulu görünüyor; kalıntılar silinmez" : response.Message;
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

    bool CanRemove() => !IsDryRun && !Progress.IsRunning && _snapshot is { IsDiff: true, ProgramStillInstalled: false } && Checked.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveLeftovers()
    {
        var chosen = Checked;
        if (_snapshot is null || chosen.Count == 0)
            return;
        if (!await _main.ConfirmAsync(
                $"{chosen.Count} kalıntı silinsin mi?",
                "Dosyalar karantinaya alınır, kayıt girdileri önce dışa aktarılır. Hizmet ve görev silme geri alınamaz.",
                "Kalıntıları sil"))
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
