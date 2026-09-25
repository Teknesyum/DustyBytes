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
        new DevArtifactExtractor(),
        new BrowserCacheExtractor(),
        new CacheExtractor(),
        new InstallerExtractor(),
        new SeriesExtractor(),
        new FilmExtractor(),
        new SystemArtifactExtractor(),
        new LargeOldFolderExtractor(),
    ];

    static readonly Dictionary<UnitKind, int> Priority = new()
    {
        [UnitKind.Game] = 0,
        [UnitKind.Program] = 1,
        [UnitKind.DevArtifact] = 2,
        [UnitKind.BrowserCache] = 3,
        [UnitKind.Cache] = 4,
        [UnitKind.Installer] = 5,
        [UnitKind.Series] = 6,
        [UnitKind.Film] = 7,
        [UnitKind.SystemArtifact] = 8,
        [UnitKind.Folder] = 9,
    };

    public static IReadOnlyList<Unit> Build(UnitContext ctx, Action<int, int>? stage = null) => Build(ctx, DefaultExtractors, stage);

    public static IReadOnlyList<Unit> Build(UnitContext ctx, IReadOnlyList<IUnitExtractor> extractors, Action<int, int>? stage = null)
    {
        var all = new List<Unit>();
        for (var i = 0; i < extractors.Count; i++)
        {
            all.AddRange(extractors[i].Extract(ctx));
            stage?.Invoke(i + 1, extractors.Count);
        }

        var ordered = all.OrderBy(u => Priority.GetValueOrDefault(u.Kind, 99)).ToList();

        var claimed = new List<string>();
        var result = new List<Unit>();

        foreach (var unit in ordered)
        {
            if (unit.Paths.Any(p => claimed.Any(c => Paths.IsUnder(p, c) || Paths.IsUnder(c, p))))
                continue;

            if (unit.Kind != UnitKind.SystemArtifact && unit.Paths.Any(p => Blocks(unit, ctx.Protected.CheckPath(p))))
                continue;

            if (unit.Paths.Any(p => IsExcludedByFlags(ctx.Root, p)))
                continue;

            foreach (var p in unit.Paths)
                claimed.Add(Paths.Normalize(p));

            result.Add(unit);
        }

        return result
            .Select(u => Scoring.WithScore(u, ctx.Now))
            .OrderByDescending(u => u.Score)
            .ToList();
    }

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
