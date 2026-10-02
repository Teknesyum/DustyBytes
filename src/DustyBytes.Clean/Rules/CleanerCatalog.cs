using System.Diagnostics;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;
using Microsoft.Win32;

namespace DustyBytes.Clean.Rules;

public sealed class CleanerCatalog
{
    public const string RegistryPrefix = "reg:";

    readonly List<CleanerRule> _rules;
    readonly ProtectedList _protected;

    public CleanerCatalog(IEnumerable<CleanerRule> rules, ProtectedList protectedList)
    {
        _rules = [.. rules];
        _protected = protectedList;
    }

    public IReadOnlyList<CleanerRule> Rules => _rules;

    public static CleanerCatalog LoadDefault(ProtectedList? protectedList = null) =>
        Load(Path.Combine(Paths.RulesDir, "cleaners"), protectedList ?? ProtectedList.LoadDefault());

    public static CleanerCatalog Load(string cleanersDir, ProtectedList protectedList)
    {
        var rules = new List<CleanerRule>();
        if (Directory.Exists(cleanersDir))
            foreach (var file in Directory.EnumerateFiles(cleanersDir, "*.json"))
                rules.Add(CleanerRule.Load(file));
        return new CleanerCatalog(rules, protectedList);
    }

    public static string RegistryEntry(string key, string? value) =>
        value is null ? RegistryPrefix + key : $"{RegistryPrefix}{key}|{value}";

    public static IReadOnlyList<RuleSelection> Selection(IEnumerable<string> optionKeys) =>
        [.. optionKeys
            .Select(item => item.Split('/', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0])
            .Select(g => new RuleSelection(g.Key, [.. g.Select(p => p[1])]))];

    public CleanerRule? Find(string ruleId) =>
        _rules.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));

    public (bool Running, string? Reason) IsRunning(CleanerRule rule)
    {
        foreach (var name in rule.Running.ProcessNames)
        {
            var procName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            try
            {
                if (Process.GetProcessesByName(procName).Length > 0)
                    return (true, $"{rule.Name} çalışıyor ({name})");
            }
            catch (InvalidOperationException)
            {
            }
        }

        foreach (var lockPattern in rule.Running.LockFiles)
        {
            foreach (var _ in PathGlob.ResolveFiles(lockPattern))
                return (true, $"{rule.Name} kilit dosyası bulundu ({lockPattern})");
        }

        return (false, null);
    }

    IEnumerable<(CleanerRule Rule, CleanerOption Option)> Selected(IEnumerable<RuleSelection> selection)
    {
        foreach (var sel in selection)
        {
            var rule = Find(sel.RuleId);
            if (rule is null)
                continue;
            foreach (var optionId in sel.OptionIds)
            {
                var option = rule.Options.FirstOrDefault(o => o.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));
                if (option is not null)
                    yield return (rule, option);
            }
        }
    }

    public IReadOnlyList<OptionPreview> Preview(IEnumerable<RuleSelection> selection)
    {
        var results = new List<OptionPreview>();
        foreach (var (rule, option) in Selected(selection))
        {
            long files = 0, bytes = 0;
            foreach (var file in PlanOption(rule.Id, option, [], CancellationToken.None))
            {
                files++;
                bytes += file.Bytes;
            }
            results.Add(new OptionPreview(rule.Id, option.Id, files, bytes));
        }
        return results;
    }

    public CleanPlan Plan(IEnumerable<RuleSelection> selection, CancellationToken ct = default)
    {
        var files = new List<PlannedFile>();
        var excluded = new List<SkippedPath>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rule, option) in Selected(selection))
            foreach (var file in PlanOption(rule.Id, option, excluded, ct))
                if (seen.Add(file.Path))
                    files.Add(file);
        return new CleanPlan(files, excluded);
    }

    IEnumerable<PlannedFile> PlanOption(string ruleId, CleanerOption option, List<SkippedPath> excluded, CancellationToken ct)
    {
        foreach (var action in option.Actions)
        {
            ct.ThrowIfCancellationRequested();
            if (action.Type == CleanActionType.RegistryDelete)
            {
                if (RegistryTargetExists(action))
                    yield return new PlannedFile(ruleId, option.Id, RegistryEntry(action.Key!, action.Value), 0, default, CleanActionType.RegistryDelete, action.Key, action.Value);
                continue;
            }

            foreach (var resolved in ResolveTargets(action))
            {
                var target = LongForm(resolved);
                var verdict = Allow(target);
                if (!verdict.Allowed)
                {
                    excluded.Add(new SkippedPath(target, verdict.Reason));
                    continue;
                }

                if (File.Exists(target))
                {
                    if (Measure(ruleId, option.Id, target) is { } single)
                        yield return single;
                    continue;
                }

                if (!Directory.Exists(target))
                    continue;

                var searchOption = action.Mode == DeleteMode.FilesOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                var ancestors = new Dictionary<string, FileAttributes?>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in EnumerateSafely(target, searchOption))
                {
                    ct.ThrowIfCancellationRequested();
                    var fileVerdict = Allow(file, ancestors);
                    if (!fileVerdict.Allowed)
                    {
                        excluded.Add(new SkippedPath(file, fileVerdict.Reason));
                        continue;
                    }
                    if (Measure(ruleId, option.Id, file) is { } planned)
                        yield return planned;
                }
            }
        }
    }

    (bool Allowed, string Reason) Allow(string path, Dictionary<string, FileAttributes?>? ancestors = null)
    {
        try
        {
            var verdict = ancestors is null ? _protected.Check(path) : CachedCheck(path, ancestors);
            return (verdict.Allowed, verdict.Reason);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return (false, "Yol denetlenemedi: " + e.Message);
        }
    }

    Verdict CachedCheck(string path, Dictionary<string, FileAttributes?> ancestors)
    {
        var byPath = _protected.CheckPath(path);
        if (!byPath.Allowed)
            return byPath;
        var provider = _protected.AttributeProvider;
        return ProtectedList.CheckFileSystem(path, p =>
        {
            if (p.Equals(path, StringComparison.OrdinalIgnoreCase))
                return provider(p);
            if (!ancestors.TryGetValue(p, out var attributes))
                ancestors[p] = attributes = provider(p);
            return attributes;
        });
    }

    static string LongForm(string path)
    {
        try
        {
            return Paths.Normalize(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return path;
        }
    }

    static PlannedFile? Measure(string ruleId, string optionId, string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new PlannedFile(ruleId, optionId, path, info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    static IEnumerable<string> EnumerateSafely(string dir, SearchOption option)
    {
        if (option == SearchOption.TopDirectoryOnly)
        {
            IEnumerable<string> top;
            try
            {
                top = Directory.EnumerateFiles(dir).ToList();
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }
            foreach (var f in top)
                yield return f;
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files;
            IEnumerable<string> subdirs;
            try
            {
                files = Directory.EnumerateFiles(current).ToList();
                subdirs = Directory.EnumerateDirectories(current).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var f in files)
                yield return f;
            foreach (var d in subdirs)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(d);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                stack.Push(d);
            }
        }
    }

    static IEnumerable<string> ResolveTargets(CleanAction action)
    {
        if (action.Path is null)
            return [];
        return action.Mode switch
        {
            DeleteMode.Glob => PathGlob.ResolveFiles(action.Path),
            _ => PathGlob.ResolveDirectories(action.Path),
        };
    }

    static bool RegistryTargetExists(CleanAction action)
    {
        if (action.Key is null)
            return false;
        var split = RegistryPathResolver.Split(action.Key);
        if (split is null)
            return false;
        try
        {
            using var key = split.Value.Root.OpenSubKey(split.Value.SubKey);
            if (key is null)
                return false;
            return action.Value is null || key.GetValue(action.Value) is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public CleanRun Execute(IEnumerable<RuleSelection> selection, IEnumerable<string> shownPaths, ICleanupDeleter deleter, CancellationToken ct = default)
    {
        var dryRun = DryRun.Enabled;
        var shown = new HashSet<string>(shownPaths, StringComparer.OrdinalIgnoreCase);
        var guard = new ShownOnlyDeleter(deleter, shown);
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<OptionExecutionResult>();
        int processed = 0, vanished = 0, locked = 0, failed = 0, notShown = 0, protectedCount = 0;
        long processedBytes = 0;
        string? error = null;

        try
        {
            foreach (var group in Selected(selection).GroupBy(s => s.Rule))
            {
                var (running, reason) = IsRunning(group.Key);
                foreach (var (rule, option) in group)
                {
                    var excluded = new List<SkippedPath>();
                    var current = PlanOption(rule.Id, option, excluded, ct).ToList();

                    if (running)
                    {
                        locked += current.Count(f => shown.Contains(f.Path) && handled.Add(f.Path));
                        results.Add(new OptionExecutionResult(rule.Id, option.Id, false, reason, 0, 0, [], dryRun));
                        continue;
                    }

                    long files = 0, bytes = 0;
                    var skipped = new List<SkippedPath>(excluded);
                    foreach (var file in current)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!shown.Contains(file.Path))
                        {
                            notShown++;
                            continue;
                        }
                        if (!handled.Add(file.Path))
                            continue;

                        var (outcome, size) = file.IsRegistry ? DeleteRegistry(file, guard, dryRun) : DeleteOneFile(file.Path, guard, dryRun);
                        switch (outcome)
                        {
                            case DeleteOutcome.Deleted:
                                processed++;
                                processedBytes += size;
                                files++;
                                bytes += size;
                                break;
                            case DeleteOutcome.Vanished:
                                vanished++;
                                break;
                            case DeleteOutcome.Locked:
                                locked++;
                                skipped.Add(new SkippedPath(file.Path, "Kullanımda"));
                                break;
                            default:
                                failed++;
                                skipped.Add(new SkippedPath(file.Path, "Silinemedi"));
                                break;
                        }
                    }

                    results.Add(new OptionExecutionResult(rule.Id, option.Id, true, null, files, bytes, skipped, dryRun));
                }
            }
        }
        catch (ShownListViolationException e)
        {
            error = e.Message;
        }

        foreach (var path in shown)
        {
            if (handled.Contains(path))
                continue;
            if (error is not null)
                failed++;
            else if (!path.StartsWith(RegistryPrefix, StringComparison.Ordinal) && !Allow(path).Allowed)
                protectedCount++;
            else
                vanished++;
        }

        var tally = new PathTally
        {
            Shown = shown.Count,
            Processed = processed,
            ProcessedBytes = processedBytes,
            Vanished = vanished,
            Protected = protectedCount,
            Locked = locked,
            Failed = failed,
            NotShown = notShown,
            Stopped = error is not null,
        };
        return new CleanRun(results, tally, error);
    }

    static (DeleteOutcome Outcome, long Bytes) DeleteRegistry(PlannedFile file, ICleanupDeleter deleter, bool dryRun)
    {
        if (dryRun)
            return (DeleteOutcome.Deleted, 0);
        return (deleter.DeleteRegistryValue(file.Key!, file.Value) ? DeleteOutcome.Deleted : DeleteOutcome.Failed, 0);
    }

    static (DeleteOutcome Outcome, long Bytes) DeleteOneFile(string path, ICleanupDeleter deleter, bool dryRun)
    {
        long size;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return (DeleteOutcome.Vanished, 0);
            size = info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (DeleteOutcome.Vanished, 0);
        }

        if (dryRun)
            return (DeleteOutcome.Deleted, size);

        return (deleter.TryDeleteFile(path), size);
    }
}
