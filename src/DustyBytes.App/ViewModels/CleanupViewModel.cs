using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum AdviceLevel
{
    Recommended,
    Optional,
    Caution,
}

public sealed record CleanAdvice(AdviceLevel Level, string Reason)
{
    public const string RecommendedText = "Önerilir";
    public const string OptionalText = "İsteğe bağlı";
    public const string CautionText = "Dikkat";
    public const string RebuildsReason = "Kendiliğinden yeniden oluşur; hiçbir şey kaybolmaz";
    public const string SessionReason = "Oturum ve kayıtlı site bilgileri silinir; geri gelmez";
    public const string TaskReason = "Bu bilgisayar için önerilir; günlük kullanım etkilenmez";
    public const string TaskOptionalReason = "Şu an önerilmez; isterseniz işaretleyin";
    public const string TaskUnavailableReason = "Şu an kullanılamıyor";

    public string Text => Level switch
    {
        AdviceLevel.Recommended => RecommendedText,
        AdviceLevel.Optional => OptionalText,
        _ => CautionText,
    };

    public bool IsRecommended => Level == AdviceLevel.Recommended;
    public bool IsOptional => Level == AdviceLevel.Optional;
    public bool IsCaution => Level == AdviceLevel.Caution;

    public static CleanAdvice Of(CleanerOption option)
    {
        var warning = string.IsNullOrWhiteSpace(option.Warning) ? null : option.Warning.Trim();
        if (option.TouchesSession)
            return new(AdviceLevel.Caution, warning ?? SessionReason);
        return warning is null ? new(AdviceLevel.Recommended, RebuildsReason) : new(AdviceLevel.Optional, warning);
    }

    public static CleanAdvice Of(SystemTaskInfo task)
    {
        if (!string.IsNullOrWhiteSpace(task.Warning))
            return new(AdviceLevel.Caution, task.Warning.Trim());
        if (SystemTaskRow.Safe(task))
            return new(AdviceLevel.Recommended, TaskReason);
        return new(AdviceLevel.Optional, task.Available ? TaskOptionalReason : TaskUnavailableReason);
    }
}

public sealed record CleanRuleGroup(CleanRuleRow Rule, IReadOnlyList<CleanOptionRow> Options);

public sealed partial class CleanOptionRow(string ruleId, CleanerOption option, bool check, Action changed) : ObservableObject
{
    public string RuleId { get; } = ruleId;
    public string Id => Option.Id;
    public CleanerOption Option { get; } = option;
    public string Label => Option.Label;
    public string? Warning => Option.Warning;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Option.Warning);
    public string Key => KeyOf(RuleId, Option.Id);
    public Explanation Explain => Option.Explanation;
    public string What => Explain.What;
    public string IfDeleted => Explain.IfDeleted;
    public string Returns => Explain.Returns;
    public string WhatTitle => Explanation.WhatTitle;
    public string IfDeletedTitle => Explanation.IfDeletedTitle;
    public string ReturnsTitle => Explanation.ReturnsTitle;
    public bool IsSession => Option.TouchesSession;
    public bool ShowWarning => HasWarning && (!IsSession || IsChecked);
    public bool ShowReason => !ShowWarning;
    public CleanAdvice Advice { get; } = CleanAdvice.Of(option);
    public bool IsRecommended => Advice.IsRecommended;
    public string ExplainButton => IsExplained ? "Gizle" : "Ne olur?";
    public string ExplainTip => "Bu nedir, silersem ne olur, geri gelir mi";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplainButton))]
    private bool _isExplained;

    [RelayCommand]
    private void ToggleExplain() => IsExplained = !IsExplained;

    public static string KeyOf(string ruleId, string optionId) => $"{ruleId}/{optionId}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWarning), nameof(ShowReason))]
    private bool _isChecked = check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long _bytes = -1;

    public string SizeText => Bytes < 0 ? "Ölçülüyor" : Format.Bytes(Bytes);

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class CleanRuleRow : ObservableObject
{
    public CleanRuleRow(CleanRuleInfo info, Action changed)
    {
        Info = info;
        Options = [.. info.Rule.Options.Select(o => new CleanOptionRow(info.Rule.Id, o, Safe(info, o), changed))];
        foreach (var option in Options.Where(o => o.IsSession))
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanOptionRow.IsChecked))
                    OnPropertyChanged(nameof(ShowSessionNote));
            };
    }

    public static bool Safe(CleanRuleInfo info, CleanerOption option) =>
        !info.Running && string.IsNullOrWhiteSpace(option.Warning) && !option.TouchesSession;

    public CleanRuleInfo Info { get; }
    public string Name => Info.Rule.Name;
    public bool Running => Info.Running;
    public string RunningText => Info.RunningReason is { Length: > 0 } r ? $"{r}; kapatınca temizlenir" : "Açık görünüyor; kapatınca temizlenir";
    public bool ShowSessionNote => Options.Any(o => o.IsSession) && !Options.Any(o => o.IsSession && o.IsChecked);
    public string SessionNote => "Giriş yaptığın siteler açık kalır";
    public string? Source => Info.Rule.Source;
    public bool HasSource => !string.IsNullOrWhiteSpace(Info.Rule.Source);
    public string SourceTip => string.Equals(Source, "Winapp2", StringComparison.OrdinalIgnoreCase)
        ? "Bu kural Winapp2 topluluk listesinden okundu (CC-BY-SA-4.0); uygulamanın yerleşik kuralı değildir, korumalı listeden yine geçer"
        : $"Kural kaynağı: {Source}";
    public IReadOnlyList<CleanOptionRow> Options { get; }
    public IReadOnlyList<CleanOptionRow> Recommended => [.. Options.Where(o => o.IsRecommended)];
    public IReadOnlyList<CleanOptionRow> Others => [.. Options.Where(o => !o.IsRecommended)];
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
    public string RestoreText => string.IsNullOrWhiteSpace(Info.RestoreLabel) ? "Geri aç" : Info.RestoreLabel;
    public bool CanReduce => !string.IsNullOrWhiteSpace(Info.AltId);
    public string ReduceText => Info.AltLabel ?? "";
    public CleanAdvice Advice { get; } = CleanAdvice.Of(info);
    public bool IsRecommended => Advice.IsRecommended;

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
    public List<ItemResult> FailedItems { get; } = [];
    public PathTally? Tally { get; set; }
    public CleanPreview? Shown { get; set; }
    public CleanPreview? Fresh { get; set; }
    public bool Stopped => Fresh is not null;

    public string? Summary => Tally?.Describe(verb: DryRun ? "silinecek" : "silinen")
        ?? (Shown is { } shown && Fresh is not null
            ? $"Gösterilen {Format.Count(shown.Count)} dosya, silinen 0; önizleme eskidi, temizlik durduruldu"
            : null);
}

public sealed record PreviewLine(string Path, string Size, string When)
{
    public static PreviewLine Of(PreviewFile file) => new(
        file.Path.StartsWith(CleanerCatalog.RegistryPrefix, StringComparison.Ordinal) ? "Kayıt defteri: " + file.Path[CleanerCatalog.RegistryPrefix.Length..] : file.Path,
        Format.Bytes(file.Bytes),
        file.LastWriteUtc == default ? "" : file.LastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")));
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
    public ObservableCollection<CleanRuleGroup> RecommendedRules { get; } = [];
    public ObservableCollection<CleanRuleGroup> OtherRules { get; } = [];
    public ObservableCollection<SystemTaskRow> RecommendedTasks { get; } = [];
    public ObservableCollection<SystemTaskRow> OtherTasks { get; } = [];

    public const string GuideText = "Hangilerini seçmeliyim? Önerilenler işaretli geldi, dokunmadan temizleyebilirsiniz.";
    public int OtherCount => OtherRules.Sum(g => g.Options.Count) + OtherTasks.Count;
    public bool HasOthers => OtherCount > 0;
    public bool HasRecommendedTasks => RecommendedTasks.Count > 0;
    public bool HasOtherTasks => OtherTasks.Count > 0;
    public bool ShowOtherTasks => ShowOthers && HasOtherTasks;
    public string OthersText => ShowOthers ? "Diğer seçenekleri gizle" : $"Diğer seçenekler ({Format.Count(OtherCount)})";
    public string OthersTip => "İsteğe bağlı ve dikkat isteyen seçenekler; işaretsiz gelir";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OthersText), nameof(ShowOtherTasks))]
    private bool _showOthers;

    [RelayCommand]
    private void ToggleOthers() => ShowOthers = !ShowOthers;

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _totalSize = "";

    [ObservableProperty]
    private string? _error;

    public const int PreviewLimit = 200;

    CancellationTokenSource? _previewCts;
    string? _previewKey;
    string? _previewFor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(PreviewLines), nameof(PreviewMore), nameof(ShowMore))]
    private CleanPreview? _preview;

    [ObservableProperty]
    private string _previewText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMore), nameof(FilesToggleText))]
    private bool _showFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public Task PreviewTask { get; private set; } = Task.CompletedTask;
    public bool HasPreviewPanel => _loaded && CheckedOptions.Any();
    public bool HasPreview => Preview is { Count: > 0 };
    public IReadOnlyList<PreviewLine> PreviewLines => Preview is null ? [] : [.. Preview.Head.Take(PreviewLimit).Select(PreviewLine.Of)];
    int Listed => Preview is null ? 0 : Math.Min(Preview.Head.Count, PreviewLimit);
    public string PreviewMore => Preview is { } p && p.Count > Listed ? $"ve {Format.Count(p.Count - Listed)} tane daha" : "";
    public bool ShowMore => ShowFiles && Preview is { } p && p.Count > Listed;
    public string FilesToggleText => ShowFiles ? "Dosya listesini gizle" : "Dosya listesini göster";
    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    [RelayCommand]
    private void ToggleFiles() => ShowFiles = !ShowFiles;

    List<string> CheckedKeys() => [.. CheckedOptions.Select(o => o.Key).Order(StringComparer.Ordinal)];

    void QueuePreview()
    {
        if (!_loaded)
            return;
        var keys = CheckedKeys();
        var key = string.Join("\n", keys);
        if (key == _previewKey)
            return;
        _previewKey = key;
        PreviewTask = RefreshPreviewAsync(keys, key);
    }

    async Task RefreshPreviewAsync(List<string> keys, string key)
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        if (keys.Count == 0)
        {
            ApplyPreview(null, key);
            return;
        }
        PreviewText = "Önizleme hazırlanıyor";
        try
        {
            var preview = await _main.Backend.PreviewCleanFilesAsync(keys, cts.Token);
            if (!cts.IsCancellationRequested)
                ApplyPreview(preview, key);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
        {
            if (!cts.IsCancellationRequested)
            {
                ApplyPreview(null, null);
                _previewKey = null;
                PreviewText = "Önizleme alınamadı: " + e.Message;
            }
        }
    }

    void ApplyPreview(CleanPreview? preview, string? key)
    {
        Preview = preview;
        _previewFor = key;
        PreviewText = preview is null ? "" : $"{Format.Count(preview.Count)} dosya, {Format.Bytes(preview.Bytes)}";
        OnPropertyChanged(nameof(HasPreviewPanel));
    }

    async Task<CleanPreview> CurrentPreviewAsync(List<string> keys)
    {
        var key = string.Join("\n", keys);
        try
        {
            await PreviewTask;
        }
        catch (OperationCanceledException)
        {
        }
        if (Preview is { } ready && _previewFor == key)
            return ready;
        _previewKey = key;
        var preview = await _main.Backend.PreviewCleanFilesAsync(keys, CancellationToken.None);
        ApplyPreview(preview, key);
        return preview;
    }

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
            RecommendedRules.Clear();
            OtherRules.Clear();
            foreach (var rule in rules)
            {
                var row = new CleanRuleRow(rule, Changed);
                Rules.Add(row);
                if (row.Recommended is { Count: > 0 } recommended)
                    RecommendedRules.Add(new CleanRuleGroup(row, recommended));
                if (row.Others is { Count: > 0 } others)
                    OtherRules.Add(new CleanRuleGroup(row, others));
            }
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
        RecommendedTasks.Clear();
        OtherTasks.Clear();
        foreach (var task in tasks)
        {
            var row = new SystemTaskRow(task, Changed);
            SystemTasks.Add(row);
            (row.IsRecommended ? RecommendedTasks : OtherTasks).Add(row);
        }
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

    bool CanReduce(SystemTaskRow? row) => row is { CanReduce: true } && !Progress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task Restore(SystemTaskRow? row) => RunExtraAsync(row, row?.Info.RestoreId, "Geri açılıyor", "Geri açılamadı: ", "yeniden açıldı");

    [RelayCommand(CanExecute = nameof(CanReduce))]
    private Task Reduce(SystemTaskRow? row) => RunExtraAsync(row, row?.Info.AltId, "Küçültülüyor", "Küçültülemedi: ", "küçültüldü");

    async Task RunExtraAsync(SystemTaskRow? row, string? restoreId, string running, string failText, string done)
    {
        if (row is null || string.IsNullOrEmpty(restoreId))
            return;
        var outcome = new CleanOutcome();
        try
        {
            await Progress.RunAsync(running, async (p, ct) =>
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
            _main.Fail(failText + e.Message);
            return;
        }
        if (outcome.Failures.Count > 0)
        {
            foreach (var failure in outcome.Failures.Take(MainViewModel.toastMax))
                _main.Fail(failure);
        }
        else
        {
            _main.Notify(outcome.DryRun ? "Prova: ayar değiştirilmedi" : $"{row.Name} {done}");
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
        QueuePreview();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(HasSystemTasks));
        OnPropertyChanged(nameof(HasRecommendedTasks));
        OnPropertyChanged(nameof(HasOtherTasks));
        OnPropertyChanged(nameof(ShowOtherTasks));
        OnPropertyChanged(nameof(OtherCount));
        OnPropertyChanged(nameof(HasOthers));
        OnPropertyChanged(nameof(OthersText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasPreviewPanel));
    }

    bool CanClean() => !Progress.IsRunning && (CheckedOptions.Any() || CheckedTasks.Any());

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task Clean()
    {
        var options = CheckedOptions.ToList();
        var tasks = CheckedTasks.ToList();
        if (options.Count == 0 && tasks.Count == 0)
            return;
        ResultText = null;
        CleanPreview? preview = null;
        if (options.Count > 0)
        {
            try
            {
                preview = await CurrentPreviewAsync(CheckedKeys());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
            {
                _main.Fail("Önizleme alınamadı; temizlik yapılmadı: " + e.Message);
                return;
            }
        }
        if (!await _main.ConfirmAsync(
                "Seçilenler temizlensin mi?",
                ConfirmText(tasks, preview),
                "Temizle"))
            return;
        var outcome = new CleanOutcome();
        try
        {
            await Progress.RunAsync("Temizlik yapılıyor", async (p, ct) =>
            {
                await SendAsync(_main.Backend, [.. options.Select(o => o.Key)], [.. tasks.Select(t => t.Id)], outcome, p, ct, preview, bytes => _main.Session.AddFreed(bytes));
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
        if (outcome.Fresh is { } fresh)
        {
            var keys = CheckedKeys();
            var key = string.Join("\n", keys);
            _previewKey = key;
            ApplyPreview(fresh, key);
            ResultText = ChangedText(outcome.Shown ?? fresh, fresh);
            _main.Fail(ResultText);
            return;
        }
        var freed = outcome.Freed;
        var failures = outcome.Failures;
        ResultText = outcome.Summary;
        if (outcome.DryRun)
        {
            _main.Notify(outcome.Summary is { } dry ? $"Prova: {dry}; hiçbir dosya silinmedi" : "Prova: temizlik ölçüldü, hiçbir dosya silinmedi");
        }
        else
        {
            _main.Notify(outcome.Summary is { } line ? $"{line}. {Format.Bytes(freed)} açıldı" : $"Temizlik bitti, {Format.Bytes(freed)} açıldı");
            _previewKey = null;
            _ = MeasureAsync();
            QueuePreview();
            if (tasks.Count > 0)
                _ = ReloadTasksAsync();
        }
        foreach (var failure in failures.Take(MainViewModel.toastMax))
            _main.Fail(failure);
    }

    static string ConfirmText(IReadOnlyList<SystemTaskRow> tasks, CleanPreview? preview)
    {
        var text = "Önbellek ve sistem artıkları karantinaya alınmadan silinir; bu işlem geri alınamaz. Açık programların dosyaları atlanır.";
        if (preview is not null)
            text += $" Yalnız listede gösterilen {Format.Count(preview.Count)} dosyaya ({Format.Bytes(preview.Bytes)}) dokunulur; sonradan çıkanlar atlanır.";
        if (tasks.Any(t => t.CanRestore || t.Id == global::DustyBytes.Clean.SystemCleanup.HibernationTask.OffId))
            text += " Hazırda bekletme ise geri alınabilir, satırındaki düğmeyle yeniden açılır.";
        return text;
    }

    public static async Task<(List<string> Options, List<string> Tasks)> SafeDefaultsAsync(IAppBackend backend, CancellationToken ct)
    {
        var (options, tasks, _) = await SafeChoicesAsync(backend, ct);
        return (options, tasks);
    }

    public static async Task<(List<string> Options, List<string> Tasks, Dictionary<string, string> Names)> SafeChoicesAsync(IAppBackend backend, CancellationToken ct)
    {
        var rules = await backend.CleanRulesAsync(ct);
        var tasks = await backend.SystemTasksAsync(ct);
        return (SafeBreakdown.SafeKeys(rules),
            [.. tasks.Where(SystemTaskRow.SilentSafe).Select(t => t.Id)],
            SafeBreakdown.Names(rules, tasks));
    }

    public static string ChangedText(CleanPreview shown, CleanPreview fresh) =>
        shown.Count == fresh.Count
            ? $"{WorkerStaleText}: liste değişti, yeni sayı {Format.Count(fresh.Count)} dosya ({Format.Bytes(fresh.Bytes)}). Temizlik yapılmadı; yeni listeye bakıp yeniden onaylayın."
            : $"{WorkerStaleText}: gösterilen {Format.Count(shown.Count)} dosyaydı, şimdi {Format.Count(fresh.Count)} dosya ({Format.Bytes(fresh.Bytes)}). Temizlik yapılmadı; yeni listeye bakıp yeniden onaylayın.";

    const string WorkerStaleText = global::DustyBytes.Clean.SystemCleanup.WorkerCleanHandlers.StaleMessage;

    public static async Task SendAsync(IAppBackend backend, IReadOnlyList<string> options, IReadOnlyList<string> tasks, CleanOutcome outcome, IProgress<TaskStep> p, CancellationToken ct, CleanPreview? shown = null, Action<long>? stepFreed = null)
    {
        if (options.Count > 0)
        {
            var preview = shown is { FromWorker: true } ? shown : await backend.PreviewCleanInWorkerAsync(options, p, ct);
            outcome.Shown = shown ?? preview;
            if (shown is not null && !preview.SameAs(shown))
            {
                outcome.Fresh = preview;
                return;
            }
            p.Report(new TaskStep("Temizlik yapılıyor", -1, $"Gösterilen {Format.Count(preview.Count)} dosya, {Format.Bytes(preview.Bytes)}"));
            var response = await backend.SendAsync(new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = [.. options], PreviewId = preview.Id, Digest = preview.Digest }, p, ct);
            if (response.Stale)
            {
                outcome.Fresh = await backend.PreviewCleanInWorkerAsync(options, p, ct);
                outcome.Failures.Add(ChangedText(outcome.Shown, outcome.Fresh));
                return;
            }
            outcome.Tally = response.Tally;
            Take(response, outcome, stepFreed);
            if (!response.Ok)
                outcome.Failures.Add(response.Message);
        }
        foreach (var task in tasks)
        {
            var response = await backend.SendAsync(new WorkerRequest { Op = Ops.SystemClean, UserApproved = true, Items = [task] }, p, ct);
            Take(response, outcome, stepFreed);
        }
    }

    static void Take(WorkerResponse response, CleanOutcome outcome, Action<long>? stepFreed)
    {
        outcome.Freed += response.FreedBytes;
        outcome.DryRun |= response.DryRun;
        var failed = response.Items.Where(i => !i.Ok).ToList();
        outcome.FailedItems.AddRange(failed);
        outcome.Failures.AddRange(failed.Select(i => $"{i.Path}: {i.Message}"));
        if (!response.DryRun && response.FreedBytes > 0)
            stepFreed?.Invoke(response.FreedBytes);
    }
}
