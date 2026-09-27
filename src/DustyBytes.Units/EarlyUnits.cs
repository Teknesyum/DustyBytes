using DustyBytes.Core;
using DustyBytes.Core.Model;
using DustyBytes.Signals;

namespace DustyBytes.Units;

public sealed record EarlyRoot(string Path, string Group);

public sealed class EarlyUnits
{
    public static IReadOnlyList<IUnitExtractor> Extractors { get; } =
    [
        new GameExtractor(),
        new ProgramExtractor(),
        new BrowserCacheExtractor(),
        new CacheExtractor(),
        new InstallerExtractor(),
        new SystemArtifactExtractor(),
    ];

    static readonly IReadOnlyList<IUnitExtractor> Claimers = [.. Extractors, new KnownContentExtractor(), new DevArtifactExtractor()];

    static readonly HashSet<UnitKind> Kinds =
    [
        UnitKind.Game,
        UnitKind.Program,
        UnitKind.BrowserCache,
        UnitKind.Cache,
        UnitKind.Installer,
        UnitKind.SystemArtifact,
    ];

    public static IReadOnlyList<Unit> Build(UnitContext ctx) =>
        [.. UnitBuilder.Build(ctx, Claimers).Where(u => Kinds.Contains(u.Kind))];

    readonly string _scanRoot;
    readonly Dictionary<string, List<EarlyRoot>> _groups = new(StringComparer.Ordinal);
    readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    public EarlyUnits(string scanRoot, IReadOnlyList<EarlyRoot> roots)
    {
        _scanRoot = Paths.Normalize(scanRoot);
        Roots = roots;
        foreach (var r in roots)
        {
            if (!_groups.TryGetValue(r.Group, out var list))
                _groups[r.Group] = list = [];
            list.Add(r);
        }
    }

    public IReadOnlyList<EarlyRoot> Roots { get; }

    public static EarlyUnits For(string scanRoot, IUsageIndex usage, IReadOnlyList<ProgramInstall> programs)
    {
        var root = Paths.Normalize(scanRoot);
        var roots = new Dictionary<string, EarlyRoot>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string group)
        {
            if (Paths.IsUnder(path, root) && !path.Equals(root, StringComparison.OrdinalIgnoreCase))
                roots.TryAdd(path, new EarlyRoot(path, group));
        }
        foreach (var game in usage.Games)
            if (Normalize(game.InstallDir) is { } dir && Directory.Exists(dir))
                Add(dir, dir);
        foreach (var program in programs)
            if (ProgramExtractor.Location(program) is { } dir && Directory.Exists(dir))
                Add(dir, dir);
        foreach (var (pattern, group) in BrowserCacheExtractor.Roots)
            foreach (var dir in Expand(root, pattern))
                Add(dir, group);
        foreach (var pattern in CacheExtractor.Roots.Concat(InstallerExtractor.Roots).Concat(SystemArtifactExtractor.Roots))
            foreach (var dir in Expand(root, pattern))
                Add(dir, dir);
        return new EarlyUnits(root, [.. roots.Values]);
    }

    public void Done(string path)
    {
        lock (_lock)
            _done.Add(Paths.Normalize(path));
    }

    public bool IsSettled(Unit unit)
    {
        var paths = unit.Kind == UnitKind.Game ? unit.Paths.Take(1) : unit.Paths;
        lock (_lock)
        {
            foreach (var p in paths)
            {
                if (!Paths.IsUnder(p, _scanRoot))
                    continue;
                var covering = Roots.Where(r => Paths.IsUnder(p, r.Path)).ToList();
                if (covering.Count == 0)
                    return false;
                foreach (var group in covering.Select(r => r.Group).Distinct(StringComparer.Ordinal))
                    if (_groups[group].Any(r => !_done.Contains(r.Path)))
                        return false;
            }
        }
        return true;
    }

    static string? Normalize(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) ? Paths.Normalize(path) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    static IEnumerable<string> Expand(string root, string pattern)
    {
        IEnumerable<string> current = [root];
        foreach (var segment in pattern.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            current = current.SelectMany(dir => Children(dir, segment)).ToList();
        return current;
    }

    static IEnumerable<string> Children(string dir, string segment)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateDirectories(segment)
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 && PathPattern.Fits(d.Name, segment))
                .Select(d => d.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }
}
