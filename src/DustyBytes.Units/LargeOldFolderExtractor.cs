using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class LargeOldFolderExtractor : IUnitExtractor
{
    const long MinBytes = 1L * 1024 * 1024 * 1024;
    const int MinAgeDays = 180;

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        Walk(ctx.Root, ctx, units);
        return units;
    }

    static bool Walk(ScanNode dir, UnitContext ctx, List<Unit> units)
    {
        var childProduced = false;
        if (dir.Children is not null)
            foreach (var child in dir.Children)
                if (child.IsDirectory)
                    childProduced |= Walk(child, ctx, units);

        if (childProduced)
            return true;

        if (dir.Parent is null)
            return false;

        if (dir.Size < MinBytes)
            return false;

        if (dir.NewestWriteTicks <= 0)
            return false;

        var ageDays = (ctx.Now - dir.NewestWrite).TotalDays;
        if (ageDays < MinAgeDays)
            return false;

        var usage = ctx.UsageIndex.ForFolder(dir.FullPath);
        var unit = new Unit
        {
            Id = UnitIdentity.Compute(UnitKind.Folder, dir.FullPath),
            Kind = UnitKind.Folder,
            Name = dir.Name,
            Paths = [dir.FullPath],
            SizeBytes = dir.Size,
            Usage = usage,
            Confidence = 0.4,
            Removal = RemovalMethod.Quarantine,
        };
        units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        return true;
    }
}
