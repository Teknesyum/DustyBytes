using DustyBytes.Core;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public static class UnitBuilder
{
    static readonly IReadOnlyList<IUnitExtractor> DefaultExtractors =
    [
        new GameExtractor(),
        new ProgramExtractor(),
        new KnownContentExtractor(),
        new DevArtifactExtractor(),
        new BrowserCacheExtractor(),
        new CacheExtractor(),
        new InstallerExtractor(),
        new OldDownloadsExtractor(),
        new SeriesExtractor(),
        new FilmExtractor(),
        new SystemArtifactExtractor(),
        new LargeOldFolderExtractor(),
    ];

    static readonly Dictionary<UnitKind, int> Priority = new()
    {
        [UnitKind.Game] = 0,
        [UnitKind.Program] = 1,
        [UnitKind.AppContent] = 2,
        [UnitKind.DevArtifact] = 3,
        [UnitKind.BrowserCache] = 4,
        [UnitKind.Cache] = 5,
        [UnitKind.Installer] = 6,
        [UnitKind.OldDownload] = 7,
        [UnitKind.Series] = 8,
        [UnitKind.Film] = 9,
        [UnitKind.SystemArtifact] = 10,
        [UnitKind.Folder] = 11,
    };

    public static IReadOnlyList<Unit> Build(UnitContext ctx, Action<int, int>? stage = null) => Build(ctx, DefaultExtractors, stage);

    public static IReadOnlyList<Unit> Build(UnitContext ctx, IReadOnlyList<IUnitExtractor> extractors, Action<int, int>? stage = null)
    {
        var parts = new List<Unit>[extractors.Count];
        var done = 0;
        Parallel.For(0, extractors.Count, i =>
        {
            parts[i] = [.. extractors[i].Extract(ctx)];
            stage?.Invoke(Interlocked.Increment(ref done), extractors.Count);
        });
        var all = parts.SelectMany(p => p).ToList();

        var ordered = all.OrderBy(u => Priority.GetValueOrDefault(u.Kind, 99)).ToList();

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var above = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Unit>();

        foreach (var candidate in ordered)
        {
            var unit = candidate;
            if (unit.Kind == UnitKind.OldDownload)
            {
                var kept = unit.Paths
                    .Where(p => !Conflicts(Paths.Normalize(p), claimed, above))
                    .Where(p => !Blocks(unit, Check(ctx.Protected, unit, p)) && !IsExcludedByFlags(ctx.Root, p))
                    .ToList();
                if (kept.Count == 0)
                    continue;
                if (kept.Count != unit.Paths.Count)
                    unit = unit with
                    {
                        Paths = kept,
                        SizeBytes = kept.Sum(p => ctx.Root.Find(p)?.Size ?? 0),
                        Reason = OldDownloadsExtractor.ReasonFor(kept.Count),
                    };
            }

            var normalized = unit.Paths.Select(Paths.Normalize).ToList();
            if (normalized.Any(p => Conflicts(p, claimed, above)))
                continue;

            if (unit.Kind != UnitKind.SystemArtifact && unit.Paths.Any(p => Blocks(unit, Check(ctx.Protected, unit, p))))
                continue;

            if (unit.Paths.Any(p => IsExcludedByFlags(ctx.Root, p)))
                continue;

            foreach (var p in normalized)
            {
                claimed.Add(p);
                foreach (var a in Ancestors(p))
                    above.Add(a);
            }

            result.Add(unit);
        }

        return result
            .Select(u => Scoring.WithScore(u, ctx.Now))
            .OrderByDescending(u => u.Score)
            .ToList();
    }

    public static IReadOnlyList<Unit> BuildDrives(UnitContext ctx, IReadOnlyList<ScanResult> drives, Action<int, int>? stage = null)
    {
        var total = Math.Max(1, drives.Count) * DefaultExtractors.Count;
        var parts = new List<IReadOnlyList<Unit>>();
        for (var i = 0; i < drives.Count; i++)
        {
            var offset = i * DefaultExtractors.Count;
            var drive = ctx with { ScanResult = drives[i], SystemDrive = i == 0 };
            parts.Add(Build(drive, stage is null ? null : (done, _) => stage(offset + done, total)));
        }
        return Merge([.. drives.Select(d => d.Root.Name)], parts);
    }

    public static IReadOnlyList<Unit> Merge(IReadOnlyList<string> roots, IReadOnlyList<IReadOnlyList<Unit>> parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Unit>();
        for (var i = 0; i < parts.Count; i++)
            foreach (var unit in parts[i])
            {
                var owner = Owner(roots, unit);
                if (owner != i && !(owner < 0 && i == 0))
                    continue;
                if (seen.Add(unit.Id))
                    result.Add(unit);
            }
        return [.. result.OrderByDescending(u => u.Score)];
    }

    public static int Owner(IReadOnlyList<string> roots, Unit unit)
    {
        if (unit.Paths.Count == 0)
            return -1;
        var best = -1;
        for (var i = 0; i < roots.Count; i++)
            if (SafeUnder(unit.Paths[0], roots[i]) && (best < 0 || roots[i].Length > roots[best].Length))
                best = i;
        return best;
    }

    static bool SafeUnder(string path, string root)
    {
        try
        {
            return Paths.IsUnder(path, root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static bool Conflicts(string normalized, HashSet<string> claimed, HashSet<string> above) =>
        above.Contains(normalized) || Ancestors(normalized).Prepend(normalized).Any(claimed.Contains);

    static IEnumerable<string> Ancestors(string path)
    {
        for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            yield return parent;
    }

    static Verdict Check(ProtectedList list, Unit unit, string path) =>
        unit.Kind == UnitKind.Game && unit.Removal == RemovalMethod.Quarantine ? list.CheckGamePath(path) : list.CheckPath(path);

    static bool Blocks(Unit unit, Verdict verdict) =>
        !verdict.Allowed && !(unit.Removal == RemovalMethod.Launcher && verdict.Badge == Badge.Launcher);

    static bool IsExcludedByFlags(ScanNode root, string path)
    {
        var node = root.Find(path);
        if (node is null)
            return false;

        const NodeFlags mask = NodeFlags.CloudPlaceholder | NodeFlags.ReparsePoint;
        return (node.Flags & mask) != 0;
    }
}
