using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class CleanOptionRow(string ruleId, CleanerOption option, bool check, Action changed) : ObservableObject
{
    public string RuleId { get; } = ruleId;
    public string Id => Option.Id;
    public CleanerOption Option { get; } = option;
    public string Label => Option.Label;
    public string? Warning => Option.Warning;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Option.Warning);
    public string Key => KeyOf(RuleId, Option.Id);

    public static string KeyOf(string ruleId, string optionId) => $"{ruleId}/{optionId}";

    [ObservableProperty]
    private bool _isChecked = check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long _bytes = -1;

    public string SizeText => Bytes < 0 ? "Ölçülüyor" : Format.Bytes(Bytes);

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class CleanRuleRow
{
    public CleanRuleRow(CleanRuleInfo info, Action changed)
    {
        Info = info;
        Options = [.. info.Rule.Options.Select(o => new CleanOptionRow(info.Rule.Id, o, Safe(info, o), changed))];
    }

    public static bool Safe(CleanRuleInfo info, CleanerOption option) => !info.Running && string.IsNullOrWhiteSpace(option.Warning);

    public CleanRuleInfo Info { get; }
    public string Name => Info.Rule.Name;
    public bool Running => Info.Running;
    public string RunningText => Info.RunningReason is { Length: > 0 } r ? $"{r}; kapatınca temizlenir" : "Açık görünüyor; kapatınca temizlenir";
    public string? Source => Info.Rule.Source;
    public bool HasSource => !string.IsNullOrWhiteSpace(Info.Rule.Source);
    public string SourceTip => string.Equals(Source, "Winapp2", StringComparison.OrdinalIgnoreCase)
        ? "Bu kural Winapp2 topluluk listesinden okundu (CC-BY-SA-4.0); uygulamanın yerleşik kuralı değildir, korumalı listeden yine geçer"
        : $"Kural kaynağı: {Source}";
    public IReadOnlyList<CleanOptionRow> Options { get; }
}

public sealed partial class SystemTaskRow(SystemTaskInfo info, Action changed) : ObservableObject
{
    public SystemTaskInfo Info { get; } = info;
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Detail => Info.Detail;
    public bool Available => Info.Available;
    public string Note => Info.Note ?? "";
    public bool HasNote => !string.IsNullOrWhiteSpace(Info.Note);
    public string SizeText => Info.Available ? Format.Bytes(Info.Bytes) : "";
    public string Tip => Info.Available ? Info.Detail : Info.Note ?? "Kullanılamıyor";
    public string Warning => Info.Warning ?? "";
    public bool HasWarning => !string.IsNullOrWhiteSpace(Info.Warning);
    public bool CanRestore => !string.IsNullOrWhiteSpace(Info.RestoreId);

    [ObservableProperty]
    private bool _isChecked = Safe(info);

    public static bool Safe(SystemTaskInfo info) => info.Available && info.Recommended;

    public static bool SilentSafe(SystemTaskInfo info) => Safe(info) && info.Silent;

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class CleanOutcome
{
    public long Freed { get; set; }
    public bool DryRun { get; set; }
    public List<string> Failures { get; } = [];
}

public sealed partial class CleanupViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    bool _loaded;

    public CleanupViewModel(MainViewModel main)
    {
        _main = main;
        Progress = main.NewProgress();
    }

    public TaskProgressViewModel Progress { get; }
    public ObservableCollection<CleanRuleRow> Rules { get; } = [];
    public ObservableCollection<SystemTaskRow> SystemTasks { get; } = [];

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _totalSize = "";

    [ObservableProperty]
    private string? _error;

    public bool Winapp2 => _main.Backend.Winapp2Present;
    public string Winapp2Text => "Ek kurallar: Winapp2 (CC-BY-SA-4.0)";
    public bool IsLoading => Progress.IsRunning;
    public bool HasRules => Rules.Count > 0 || SystemTasks.Count > 0;
    public bool HasSystemTasks => SystemTasks.Count > 0;
    public bool IsEmpty => _loaded && !HasRules && Error is null && !IsLoading;
    public bool HasError => Error is not null && !IsLoading;
    public string DisabledTip => "Önce en az bir seçenek işaretleyin";

    IEnumerable<CleanOptionRow> CheckedOptions => Rules.Where(r => !r.Running).SelectMany(r => r.Options).Where(o => o.IsChecked);
    IEnumerable<SystemTaskRow> CheckedTasks => SystemTasks.Where(t => t.IsChecked && t.Available);

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
            var (rules, tasks) = await Progress.RunAsync("Temizlik kuralları ölçülüyor", async (p, ct) =>
            {
                p.Report(new TaskStep("Kurallar okunuyor", 5, null));
                var rules = await _main.Backend.CleanRulesAsync(ct);
                p.Report(new TaskStep("Sistem artıkları ölçülüyor", 30, null));
                var tasks = await _main.Backend.SystemTasksAsync(ct);
                return (rules, tasks);
            });
            Rules.Clear();
            foreach (var rule in rules)
                Rules.Add(new CleanRuleRow(rule, Changed));
            FillTasks(tasks);
            _loaded = true;
            Changed();
            await MeasureAsync();
        }
        catch (OperationCanceledException)
        {
            Error = "Ölçüm iptal edildi";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = "Kurallar okunamadı: " + e.Message;
        }
        Raise();
    }

    void FillTasks(IReadOnlyList<SystemTaskInfo> tasks)
    {
        SystemTasks.Clear();
        foreach (var task in tasks)
            SystemTasks.Add(new SystemTaskRow(task, Changed));
    }

    async Task ReloadTasksAsync()
    {
        try
        {
            FillTasks(await _main.Backend.SystemTasksAsync(CancellationToken.None));
            Changed();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _main.Fail("Sistem görevleri yeniden ölçülemedi: " + e.Message);
        }
    }

    bool CanRestore(SystemTaskRow? row) => row is { CanRestore: true } && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task Restore(SystemTaskRow? row)
    {
        if (row?.Info.RestoreId is not { Length: > 0 } restoreId)
            return;
        var outcome = new CleanOutcome();
        try
        {
            await Progress.RunAsync("Geri açılıyor", async (p, ct) =>
            {
                await SendAsync(_main.Backend, [], [restoreId], outcome, p, ct);
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            _main.Notify("İşlem iptal edildi");
            return;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Geri açılamadı: " + e.Message);
            return;
        }
        if (outcome.Failures.Count > 0)
        {
            foreach (var failure in outcome.Failures.Take(MainViewModel.toastMax))
                _main.Fail(failure);
        }
        else
        {
            _main.Notify(outcome.DryRun ? "Prova: ayar değiştirilmedi" : $"{row.Name} yeniden açıldı");
        }
        await ReloadTasksAsync();
    }

    async Task MeasureAsync()
    {
        var selection = Rules.Select(r => new RuleSelection(r.Info.Rule.Id, [.. r.Options.Select(o => o.Id)])).ToList();
        if (selection.Count == 0)
            return;
        var previews = await _main.Backend.PreviewCleanAsync(selection, CancellationToken.None);
        foreach (var rule in Rules)
            foreach (var option in rule.Options)
                option.Bytes = previews.FirstOrDefault(p => p.RuleId == rule.Info.Rule.Id && p.OptionId == option.Id)?.Bytes ?? 0;
        Changed();
    }

    void Changed()
    {
        var bytes = CheckedOptions.Sum(o => Math.Max(0, o.Bytes)) + CheckedTasks.Sum(t => t.Info.Bytes);
        var count = CheckedOptions.Count() + CheckedTasks.Count();
        var olculmedi = CheckedOptions.Any(o => o.Bytes < 0);
        TotalText = count == 0 ? "Hiçbir seçenek işaretlenmedi" : $"{Format.Count(count)} seçenek · {(olculmedi ? "ölçülenler en az" : "yaklaşık")} ";
        TotalSize = count == 0 ? "" : Format.Bytes(bytes);
        CleanCommand.NotifyCanExecuteChanged();
        Raise();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(HasSystemTasks));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    bool CanClean() => !Progress.IsRunning && (CheckedOptions.Any() || CheckedTasks.Any());

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task Clean()
    {
        var options = CheckedOptions.ToList();
        var tasks = CheckedTasks.ToList();
        if (options.Count == 0 && tasks.Count == 0)
            return;
        if (!await _main.ConfirmAsync(
                "Seçilenler temizlensin mi?",
                ConfirmText(tasks),
                "Temizle"))
            return;
        var outcome = new CleanOutcome();
        try
        {
            await Progress.RunAsync("Temizlik yapılıyor", async (p, ct) =>
            {
                await SendAsync(_main.Backend, [.. options.Select(o => o.Key)], [.. tasks.Select(t => t.Id)], outcome, p, ct);
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            _main.Notify("Temizlik iptal edildi");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            _main.Fail("Temizlik yapılamadı: " + e.Message);
            return;
        }
        var freed = outcome.Freed;
        var failures = outcome.Failures;
        if (outcome.DryRun)
        {
            _main.Notify("Prova: temizlik ölçüldü, hiçbir dosya silinmedi");
        }
        else
        {
            if (freed > 0)
                _main.Session.AddFreed(freed);
            _main.Notify($"Temizlik bitti, {Format.Bytes(freed)} açıldı");
            _ = MeasureAsync();
            if (tasks.Count > 0)
                _ = ReloadTasksAsync();
        }
        foreach (var failure in failures.Take(MainViewModel.toastMax))
            _main.Fail(failure);
    }

    static string ConfirmText(IReadOnlyList<SystemTaskRow> tasks)
    {
        var text = "Önbellek ve sistem artıkları karantinaya alınmadan silinir; bu işlem geri alınamaz. Açık programların dosyaları atlanır.";
        if (tasks.Any(t => t.CanRestore || t.Id == global::DustyBytes.Clean.SystemCleanup.HibernationTask.OffId))
            text += " Hazırda bekletme ise geri alınabilir, satırındaki düğmeyle yeniden açılır.";
        return text;
    }

    public static async Task<(List<string> Options, List<string> Tasks)> SafeDefaultsAsync(IAppBackend backend, CancellationToken ct)
    {
        var rules = await backend.CleanRulesAsync(ct);
        var tasks = await backend.SystemTasksAsync(ct);
        return ([.. rules.SelectMany(r => r.Rule.Options.Where(o => CleanRuleRow.Safe(r, o)).Select(o => CleanOptionRow.KeyOf(r.Rule.Id, o.Id)))],
            [.. tasks.Where(SystemTaskRow.SilentSafe).Select(t => t.Id)]);
    }

    public static async Task SendAsync(IAppBackend backend, IReadOnlyList<string> options, IReadOnlyList<string> tasks, CleanOutcome outcome, IProgress<TaskStep> p, CancellationToken ct)
    {
        if (options.Count > 0)
        {
            var response = await backend.SendAsync(new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = [.. options] }, p, ct);
            outcome.Freed += response.FreedBytes;
            outcome.DryRun |= response.DryRun;
            outcome.Failures.AddRange(response.Items.Where(i => !i.Ok).Select(i => $"{i.Path}: {i.Message}"));
            if (!response.Ok)
                outcome.Failures.Add(response.Message);
        }
        if (tasks.Count > 0)
        {
            var response = await backend.SendAsync(new WorkerRequest { Op = Ops.SystemClean, UserApproved = true, Items = [.. tasks] }, p, ct);
            outcome.Freed += response.FreedBytes;
            outcome.DryRun |= response.DryRun;
            outcome.Failures.AddRange(response.Items.Where(i => !i.Ok).Select(i => $"{i.Path}: {i.Message}"));
        }
    }
}
