using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class CacheExtractor : IUnitExtractor
{
    static readonly string[] TempPatterns = [@"Windows\Temp", @"Users\*\AppData\Local\Temp"];
    static readonly string[] CrashDumpPatterns = [@"Users\*\AppData\Local\CrashDumps"];
    static readonly string[] WerPatterns = [@"ProgramData\Microsoft\Windows\WER"];
    static readonly string[] ThumbcacheDirPatterns = [@"Users\*\AppData\Local\Microsoft\Windows\Explorer"];

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var pattern in TempPatterns)
            foreach (var node in PathPattern.Match(ctx.Root, pattern))
                AddFolderUnit(units, ctx, node, "Geçici dosyalar");

        foreach (var pattern in CrashDumpPatterns)
            foreach (var node in PathPattern.Match(ctx.Root, pattern))
                AddFolderUnit(units, ctx, node, "Çökme dökümleri");

        foreach (var pattern in WerPatterns)
            foreach (var node in PathPattern.Match(ctx.Root, pattern))
                AddFolderUnit(units, ctx, node, "Windows hata bildirimi");

        foreach (var pattern in ThumbcacheDirPatterns)
            foreach (var node in PathPattern.Match(ctx.Root, pattern))
                AddThumbcacheUnit(units, ctx, node);

        return units;
    }

    static void AddFolderUnit(List<Unit> units, UnitContext ctx, ScanNode node, string label)
    {
        if (node.Size <= 0)
            return;
        var usage = ctx.UsageIndex.ForFolder(node.FullPath);
        var unit = new Unit
        {
            Id = UnitIdentity.Compute(UnitKind.Cache, node.FullPath),
            Kind = UnitKind.Cache,
            Name = $"{label} ({UserSegment(node)})",
            Paths = [node.FullPath],
            SizeBytes = node.Size,
            Usage = usage,
            Confidence = 1.0,
            Removal = RemovalMethod.DirectDelete,
        };
        units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
    }

    static void AddThumbcacheUnit(List<Unit> units, UnitContext ctx, ScanNode explorerDir)
    {
        var files = (explorerDir.Children ?? []).Where(c =>
            !c.IsDirectory &&
            (c.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
             c.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))).ToList();
        if (files.Count == 0)
            return;

        var size = files.Sum(f => f.Size);
        if (size <= 0)
            return;

        var usage = ctx.UsageIndex.ForFolder(explorerDir.FullPath);
        var unit = new Unit
        {
            Id = UnitIdentity.Compute(UnitKind.Cache, explorerDir.FullPath + "\\thumbcache"),
            Kind = UnitKind.Cache,
            Name = $"Küçük resim önbelleği ({UserSegment(explorerDir)})",
            Paths = files.Select(f => f.FullPath).ToList(),
            SizeBytes = size,
            Usage = usage,
            Confidence = 1.0,
            Removal = RemovalMethod.DirectDelete,
        };
        units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
    }

    static string UserSegment(ScanNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (n.Parent is { Name: "Users" })
                return n.Name;
        return "sistem";
    }
}
