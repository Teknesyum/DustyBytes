using DustyBytes.Core;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class CloudCopyExtractor : IUnitExtractor
{
    public const long MinFileBytes = 1024 * 1024;
    public const long MinUnitBytes = 100L * 1024 * 1024;
    public const int MinAgeDays = 180;

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var syncRoot in ctx.Protected.SyncRoots)
        {
            if (!seen.Add(syncRoot) || ctx.Root.Find(syncRoot) is not { IsDirectory: true, Children: { } children } root)
                continue;
            var loose = new List<ScanNode>();
            foreach (var child in children)
            {
                if (child.IsDirectory)
                    Add(units, ctx, child.FullPath, child.Name, child.Descendants().Where(n => IsCandidate(n, ctx)));
                else if (IsCandidate(child, ctx))
                    loose.Add(child);
            }
            Add(units, ctx, root.FullPath, root.Name, loose);
        }
        return units;
    }

    public static bool IsCandidate(ScanNode node, UnitContext ctx) =>
        !node.IsDirectory
        && node.ReparseTag == "cloud"
        && (node.Flags & NodeFlags.CloudPlaceholder) == 0
        && node.LogicalSize >= MinFileBytes
        && node.Size > 0
        && node.LastWriteTicks > 0
        && (ctx.Now - node.LastWrite).TotalDays >= MinAgeDays;

    static void Add(List<Unit> units, UnitContext ctx, string path, string name, IEnumerable<ScanNode> candidates)
    {
        var files = candidates.Where(n => ctx.CloudEligible?.Invoke(n.FullPath) ?? true).ToList();
        var size = files.Sum(n => n.Size);
        if (files.Count == 0 || size < MinUnitBytes)
            return;
        var newest = files.Max(n => n.LastWrite);
        var unit = new Unit
        {
            Id = UnitIdentity.Compute(UnitKind.CloudCopy, path),
            Kind = UnitKind.CloudCopy,
            Name = name,
            Paths = [.. files.Select(n => n.FullPath)],
            SizeBytes = size,
            Usage = new UsageSignal(newest, "Dosya tarihi", 0.5),
            Confidence = 0.6,
            Removal = RemovalMethod.CloudOnly,
            ContainsUserData = true,
        };
        units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
    }
}
