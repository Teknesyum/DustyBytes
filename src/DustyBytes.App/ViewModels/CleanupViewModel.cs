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
    public Explanation Explain => Option.Explanation;
    public string What => Explain.What;
    public string IfDeleted => Explain.IfDeleted;
    public string Returns => Explain.Returns;
    public string WhatTitle => Explanation.WhatTitle;
    public string IfDeletedTitle => Explanation.IfDeletedTitle;
    public string ReturnsTitle => Explanation.ReturnsTitle;
    public bool IsSession => Option.TouchesSession;
    public bool ShowWarning => HasWarning && (!IsSession || IsChecked);
    public string ExplainButton => IsExplained ? "Gizle" : "Ne olur?";
    public string ExplainTip => "Bu nedir, silersem ne olur, geri gelir mi";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplainButton))]
    private bool _isExplained;

    [RelayCommand]
    private void ToggleExplain() => IsExplained = !IsExplained;

    public static string KeyOf(string ruleId, string optionId) => $"{ruleId}/{optionId}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWarning))]
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
    public PathTally? Tally { get; set; }
    public string? Summary => Tally?.Describe(verb: DryRun ? "silinecek" : "silinen");
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
    public IReadOnlyList<PreviewLine> PreviewLines => Preview is null ? [] : [.. Preview.Files.Take(PreviewLimit).Select(PreviewLine.Of)];
    public string PreviewMore => Preview is { Count: > PreviewLimit } p ? $"ve {Format.Count(p.Count - PreviewLimit)} tane daha" : "";
    public bool ShowMore => ShowFiles && Preview is { Count: > PreviewLimit };
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
                await SendAsync(_main.Backend, [.. options.Select(o => o.Key)], [.. tasks.Select(t => t.Id)], outcome, p, ct, preview);
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
        ResultText = outcome.Summary;
        if (outcome.DryRun)
        {
            _main.Notify(outcome.Summary is { } dry ? $"Prova: {dry}; hiçbir dosya silinmedi" : "Prova: temizlik ölçüldü, hiçbir dosya silinmedi");
        }
        else
        {
            if (freed > 0)
                _main.Session.AddFreed(freed);
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
        var rules = await backend.CleanRulesAsync(ct);
        var tasks = await backend.SystemTasksAsync(ct);
        return ([.. rules.SelectMany(r => r.Rule.Options.Where(o => CleanRuleRow.Safe(r, o)).Select(o => CleanOptionRow.KeyOf(r.Rule.Id, o.Id)))],
            [.. tasks.Where(SystemTaskRow.SilentSafe).Select(t => t.Id)]);
    }

    public static async Task<long> SafeBytesAsync(IAppBackend backend, CancellationToken ct)
    {
        var rules = await backend.CleanRulesAsync(ct);
        var tasks = await backend.SystemTasksAsync(ct);
        var selection = rules
            .Select(r => new RuleSelection(r.Rule.Id, [.. r.Rule.Options.Where(o => CleanRuleRow.Safe(r, o)).Select(o => o.Id)]))
            .Where(s => s.OptionIds.Count > 0)
            .ToList();
        IReadOnlyList<OptionPreview> previews = selection.Count == 0 ? [] : await backend.PreviewCleanAsync(selection, ct);
        var chosen = selection.SelectMany(s => s.OptionIds.Select(o => CleanOptionRow.KeyOf(s.RuleId, o))).ToHashSet(StringComparer.Ordinal);
        return previews.Where(p => chosen.Contains(CleanOptionRow.KeyOf(p.RuleId, p.OptionId))).Sum(p => Math.Max(0, p.Bytes))
            + tasks.Where(SystemTaskRow.SilentSafe).Sum(t => Math.Max(0, t.Bytes));
    }

    public static async Task SendAsync(IAppBackend backend, IReadOnlyList<string> options, IReadOnlyList<string> tasks, CleanOutcome outcome, IProgress<TaskStep> p, CancellationToken ct, CleanPreview? preview = null)
    {
        if (options.Count > 0)
        {
            preview ??= await backend.PreviewCleanFilesAsync(options, ct);
            var response = await backend.SendAsync(new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = [.. options], Paths = preview.Paths(), Digest = preview.Digest }, p, ct);
            outcome.Tally = response.Tally;
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
