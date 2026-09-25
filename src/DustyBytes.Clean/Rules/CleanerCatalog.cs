using System.Diagnostics;
using DustyBytes.Core;
using DustyBytes.Core.Protection;
using Microsoft.Win32;

namespace DustyBytes.Clean.Rules;

public sealed class CleanerCatalog
{
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

    public IReadOnlyList<OptionPreview> Preview(IEnumerable<RuleSelection> selection)
    {
        var results = new List<OptionPreview>();
        foreach (var sel in selection)
        {
            var rule = Find(sel.RuleId);
            if (rule is null)
                continue;
            foreach (var optionId in sel.OptionIds)
            {
                var option = rule.Options.FirstOrDefault(o => o.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));
                if (option is null)
                    continue;
                var (files, bytes) = PreviewOption(option);
                results.Add(new OptionPreview(rule.Id, option.Id, files, bytes));
            }
        }
        return results;
    }

    (long Files, long Bytes) PreviewOption(CleanerOption option)
    {
        long files = 0, bytes = 0;
        foreach (var action in option.Actions)
        {
            if (action.Type == CleanActionType.RegistryDelete)
            {
                if (RegistryTargetExists(action))
                    files++;
                continue;
            }

            if (action.Path is null)
                continue;

            foreach (var path in ResolveTargets(action))
            {
                if (!_protected.Check(path).Allowed)
                    continue;
                (var f, var b) = MeasurePath(path, action.Mode);
                files += f;
                bytes += b;
            }
        }
        return (files, bytes);
    }

    static (long Files, long Bytes) MeasurePath(string path, DeleteMode? mode)
    {
        try
        {
            if (File.Exists(path))
                return (1, new FileInfo(path).Length);

            if (!Directory.Exists(path))
                return (0, 0);

            long files = 0, bytes = 0;
            var searchOption = mode == DeleteMode.FilesOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
            foreach (var f in EnumerateSafely(path, searchOption))
            {
                try
                {
                    var info = new FileInfo(f);
                    files++;
                    bytes += info.Length;
                }
                catch (IOException)
                {
                }
            }
            return (files, bytes);
        }
        catch (IOException)
        {
            return (0, 0);
        }
        catch (UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    static IEnumerable<string> EnumerateSafely(string dir, SearchOption option)
    {
        if (option == SearchOption.TopDirectoryOnly)
        {
            IEnumerable<string> top;
            try
            {
                top = Directory.EnumerateFiles(dir);
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
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
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

    public IReadOnlyList<OptionExecutionResult> Execute(IEnumerable<RuleSelection> selection, ICleanupDeleter deleter)
    {
        var dryRun = DryRun.Enabled;
        var results = new List<OptionExecutionResult>();

        foreach (var sel in selection)
        {
            var rule = Find(sel.RuleId);
            if (rule is null)
                continue;

            var (running, reason) = IsRunning(rule);

            foreach (var optionId in sel.OptionIds)
            {
                var option = rule.Options.FirstOrDefault(o => o.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));
                if (option is null)
                    continue;

                if (running)
                {
                    results.Add(new OptionExecutionResult(rule.Id, option.Id, false, reason, 0, 0, [], dryRun));
                    continue;
                }

                results.Add(ExecuteOption(rule.Id, option, deleter, dryRun));
            }
        }

        return results;
    }

    OptionExecutionResult ExecuteOption(string ruleId, CleanerOption option, ICleanupDeleter deleter, bool dryRun)
    {
        long deletedFiles = 0, deletedBytes = 0;
        var skipped = new List<SkippedPath>();

        foreach (var action in option.Actions)
        {
            if (action.Type == CleanActionType.RegistryDelete)
            {
                if (!RegistryTargetExists(action))
                    continue;
                if (dryRun)
                {
                    deletedFiles++;
                    continue;
                }
                if (deleter.DeleteRegistryValue(action.Key!, action.Value))
                    deletedFiles++;
                continue;
            }

            foreach (var path in ResolveTargets(action))
            {
                var verdict = _protected.Check(path);
                if (!verdict.Allowed)
                {
                    skipped.Add(new SkippedPath(path, verdict.Reason));
                    continue;
                }

                if (File.Exists(path))
                {
                    DeleteOneFile(path, deleter, dryRun, ref deletedFiles, ref deletedBytes, skipped);
                    continue;
                }

                if (!Directory.Exists(path))
                    continue;

                var searchOption = action.Mode == DeleteMode.FilesOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (var file in EnumerateSafely(path, searchOption))
                {
                    var fileVerdict = _protected.Check(file);
                    if (!fileVerdict.Allowed)
                    {
                        skipped.Add(new SkippedPath(file, fileVerdict.Reason));
                        continue;
                    }
                    DeleteOneFile(file, deleter, dryRun, ref deletedFiles, ref deletedBytes, skipped);
                }
            }
        }

        return new OptionExecutionResult(ruleId, option.Id, true, null, deletedFiles, deletedBytes, skipped, dryRun);
    }

    static void DeleteOneFile(string path, ICleanupDeleter deleter, bool dryRun, ref long deletedFiles, ref long deletedBytes, List<SkippedPath> skipped)
    {
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return;
        }

        if (dryRun)
        {
            deletedFiles++;
            deletedBytes += size;
            return;
        }

        if (deleter.DeleteFile(path))
        {
            deletedFiles++;
            deletedBytes += size;
        }
        else
        {
            skipped.Add(new SkippedPath(path, "Silinemedi"));
        }
    }
}
