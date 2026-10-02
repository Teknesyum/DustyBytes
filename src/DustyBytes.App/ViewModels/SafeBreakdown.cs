using DustyBytes.App.Services;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum SafeItemKind
{
    Rule,
    Task,
    Unit,
}

public sealed record SafeFile(string Path, long Bytes, DateTime LastWriteUtc);

public sealed record SafeItem(string Key, SafeItemKind Kind, string Name, string What, int? Count, long Bytes, IReadOnlyList<SafeFile> Files);

public static class SafeBreakdown
{
    public const int Shown = 4;
    public const int FileLimit = 10;

    public static string TaskKey(string id) => "task:" + id;

    public static string UnitKey(string id) => "unit:" + id;

    public static List<string> SafeKeys(IEnumerable<CleanRuleInfo> rules) =>
        [.. rules.SelectMany(r => r.Rule.Options.Where(o => CleanRuleRow.Safe(r, o)).Select(o => CleanOptionRow.KeyOf(r.Rule.Id, o.Id)))];

    public static List<SafeItem> Rules(IReadOnlyList<CleanRuleInfo> rules, IReadOnlyList<SystemTaskInfo> tasks, CleanPreview? preview, IReadOnlyList<OptionPreview> estimates)
    {
        var items = new List<SafeItem>();
        foreach (var info in rules)
            foreach (var option in info.Rule.Options.Where(o => CleanRuleRow.Safe(info, o)))
            {
                var key = CleanOptionRow.KeyOf(info.Rule.Id, option.Id);
                int? count = null;
                long bytes = 0;
                if (preview is not null)
                {
                    var total = preview.Options.FirstOrDefault(t => string.Equals(t.Option, key, StringComparison.OrdinalIgnoreCase));
                    count = total?.Count ?? 0;
                    bytes = total?.Bytes ?? 0;
                }
                else if (estimates.FirstOrDefault(e => string.Equals(CleanOptionRow.KeyOf(e.RuleId, e.OptionId), key, StringComparison.OrdinalIgnoreCase)) is { } estimate)
                {
                    count = (int)Math.Clamp(estimate.FileCount, 0, int.MaxValue);
                    bytes = estimate.Bytes;
                }
                items.Add(new SafeItem(key, SafeItemKind.Rule, Name(info.Rule.Name, option.Label), What(option), count, Math.Max(0, bytes), preview is null ? [] : FilesFor(preview, key)));
            }
        foreach (var task in tasks.Where(SystemTaskRow.SilentSafe))
            items.Add(new SafeItem(TaskKey(task.Id), SafeItemKind.Task, task.Name, task.Detail, null, Math.Max(0, task.Bytes), []));
        return items;
    }

    public static List<SafeItem> Units(IEnumerable<Unit> units) =>
    [
        .. units.Where(TourViewModel.Silent).Select(u => new SafeItem(
            UnitKey(u.Id),
            SafeItemKind.Unit,
            $"{u.Name} ({KindText.Label(u)})",
            UnitKindInfo.Explain(u.Kind).What,
            null,
            Math.Max(0, u.SizeBytes),
            [.. u.Paths.Select(p => new SafeFile(p, u.Paths.Count == 1 ? Math.Max(0, u.SizeBytes) : -1, default))])),
    ];

    public static List<SafeFile> FilesFor(CleanPreview preview, string key, int limit = FileLimit) =>
    [
        .. preview.Head
            .Where(f => string.Equals(f.Option, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Bytes)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(f => new SafeFile(f.Path, f.Bytes, f.LastWriteUtc)),
    ];

    public static List<SafeItem> Order(IEnumerable<SafeItem> items) =>
        [.. items.Where(i => i.Bytes > 0 || i.Count > 0).OrderByDescending(i => i.Bytes).ThenBy(i => i.Name, StringComparer.CurrentCulture)];

    public static long Total(IEnumerable<SafeItem> items, IReadOnlySet<string> skipped) =>
        items.Where(i => !skipped.Contains(i.Key)).Sum(i => Math.Max(0, i.Bytes));

    public static (List<string> Options, List<string> Tasks) Scope(IEnumerable<string> options, IEnumerable<string> tasks, IReadOnlySet<string> skipped) =>
        ([.. options.Where(o => !skipped.Contains(o))], [.. tasks.Where(t => !skipped.Contains(TaskKey(t)))]);

    public static async Task<List<SafeItem>> LoadAsync(IAppBackend backend, CancellationToken ct)
    {
        var rules = await backend.CleanRulesAsync(ct);
        var tasks = await backend.SystemTasksAsync(ct);
        var keys = SafeKeys(rules);
        CleanPreview? preview = null;
        IReadOnlyList<OptionPreview> estimates = [];
        if (keys.Count > 0)
        {
            try
            {
                preview = await backend.PreviewCleanFilesAsync(keys, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or Worker.WorkerStartException)
            {
                preview = null;
            }
            if (preview is null)
                estimates = await backend.PreviewCleanAsync(CleanerCatalog.Selection(keys), ct);
        }
        return Rules(rules, tasks, preview, estimates);
    }

    static string Name(string rule, string option) => string.IsNullOrWhiteSpace(option) ? rule : $"{rule} · {option}";

    static string What(CleanerOption option) =>
        string.IsNullOrWhiteSpace(option.What) ? UnitKindInfo.Explain(UnitKind.Cache).What : option.What;
}
