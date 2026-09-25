using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class SystemArtifactExtractor : IUnitExtractor
{
    static readonly (string Pattern, string Name)[] Targets =
    [
        (@"Windows\SoftwareDistribution\Download", "Windows Update indirmeleri"),
        (@"Windows.old", "Önceki Windows kurulumu"),
        (@"$Windows.~BT", "Windows kurulum geçici dosyaları"),
        (@"Windows\SoftwareDistribution\DeliveryOptimization", "Delivery Optimization önbelleği"),
    ];

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var (pattern, name) in Targets)
        {
            var node = ctx.Root.ResolveRelative(pattern);
            if (node is null || node.Size <= 0)
                continue;

            var usage = ctx.UsageIndex.ForFolder(node.FullPath);
            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.SystemArtifact, node.FullPath),
                Kind = UnitKind.SystemArtifact,
                Name = name,
                Paths = [node.FullPath],
                SizeBytes = node.Size,
                Usage = usage,
                Confidence = 1.0,
                Removal = RemovalMethod.SystemTool,
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }
}
