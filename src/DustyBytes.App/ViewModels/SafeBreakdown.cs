using DustyBytes.App.Services;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public enum SafeItemKind
{
    Rule,
    Task,
    Group,
}

public sealed record SafeFile(string Path, long Bytes, DateTime LastWriteUtc);

public sealed record SafeItem(string Key, SafeItemKind Kind, string Name, string What, int? Count, long Bytes, IReadOnlyList<SafeFile> Files)
{
    public IReadOnlyList<SafeUnit> Units { get; init; } = [];
    public string Noun { get; init; } = "";
}

public sealed record SafeUnit(string Key, string Name, string Place, string Target, long Bytes);

public static class SafeBreakdown
{
    public const int Shown = 4;
    public const int FileLimit = 10;
    public const int UnitPage = 20;

    public static string TaskKey(string id) => "task:" + id;

    public static string UnitKey(string id) => "unit:" + id;

    public static string GroupKey(UnitKind kind, string? label) => label is null ? $"group:{kind}" : $"group:{kind}:{label}";

    public static List<string> SafeKeys(IEnumerable<CleanRuleInfo> rules) =>
        [.. rules.SelectMany(r => r.Rule.Options.Where(o => CleanRuleRow.Safe(r, o)).Select(o => CleanOptionRow.KeyOf(r.Rule.Id, o.Id)))];

    public static Dictionary<string, string> Names(IEnumerable<CleanRuleInfo> rules, IEnumerable<SystemTaskInfo> tasks)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in rules)
            foreach (var option in info.Rule.Options)
                names[CleanOptionRow.KeyOf(info.Rule.Id, option.Id)] = Name(info.Rule.Name, option.Label);
        foreach (var task in tasks)
            names[task.Id] = task.Name;
        return names;
    }

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
        .. units.Where(TourViewModel.Silent)
            .GroupBy(u => (u.Kind, u.Label))
            .Select(g =>
            {
                var members = g.OrderByDescending(u => u.SizeBytes).ThenBy(u => u.Name, StringComparer.CurrentCulture).Select(Member).ToList();
                var (title, noun) = GroupTitle(g.Key.Kind, g.Key.Label);
                return new SafeItem(
                    GroupKey(g.Key.Kind, g.Key.Label),
                    SafeItemKind.Group,
                    $"{title} · {Format.Count(members.Count)} {noun}",
                    UnitKindInfo.Explain(g.Key.Kind).What,
                    null,
                    members.Sum(m => m.Bytes),
                    [])
                {
                    Units = members,
                    Noun = noun,
                };
            }),
    ];

    public static SafeUnit Member(Unit unit)
    {
        var target = unit.Paths.Count > 0 ? unit.Paths[0] : "";
        var plain = Plain(unit.Name);
        var owner = Owner(plain, target);
        return new SafeUnit(UnitKey(unit.Id), owner is null ? plain : $"{owner} › {plain}", Shorten(target), target, Math.Max(0, unit.SizeBytes));
    }

    public static string Plain(string name)
    {
        var text = name.Trim();
        if (!text.EndsWith(')'))
            return text;
        var open = text.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
            return text;
        var inner = text[(open + 2)..^1].Trim();
        return inner.Length == 0 || inner.Contains('(') ? text : $"{text[..open].TrimEnd()} · {inner}";
    }

    public static string Shorten(string path, int keep = 3)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        var (root, parts) = Segments(path);
        return parts.Length <= keep + 1 ? path : root + "…\\" + string.Join('\\', parts[^keep..]);
    }

    static (string Root, string[] Parts) Segments(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        return (root, path[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries));
    }

    static string? Owner(string plain, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        var head = plain.Split(" · ")[0];
        var (_, parts) = Segments(target);
        for (var i = parts.Length - 1; i > 0; i--)
            if (string.Equals(parts[i], head, StringComparison.OrdinalIgnoreCase))
                return parts[i - 1];
        return null;
    }

    static (string Title, string Noun) GroupTitle(UnitKind kind, string? label) => kind switch
    {
        _ when label is not null => (label, "klasör"),
        UnitKind.DevArtifact => ("Geliştirici derleme klasörleri", "proje"),
        UnitKind.Cache => ("Uygulama önbellekleri", "klasör"),
        UnitKind.BrowserCache => ("Tarayıcı önbellekleri", "klasör"),
        _ => (KindText.Label(kind), "klasör"),
    };

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
        items.Sum(i => Active(i, skipped));

    public static long Active(SafeItem item, IReadOnlySet<string> skipped) =>
        item.Kind == SafeItemKind.Group
            ? item.Units.Where(u => !skipped.Contains(u.Key)).Sum(u => Math.Max(0, u.Bytes))
            : skipped.Contains(item.Key) ? 0 : Math.Max(0, item.Bytes);

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
