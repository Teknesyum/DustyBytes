using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class DevArtifactExtractor(DevArtifactRules? rules = null) : IUnitExtractor
{
    readonly DevArtifactRules _rules = rules ?? DevArtifactRules.LoadDefault();

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        var skip = new HashSet<ScanNode>();

        var stack = new Stack<ScanNode>();
        stack.Push(ctx.Root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (skip.Contains(dir))
                continue;

            foreach (var eco in _rules.Ecosystems)
            {
                if (!eco.Markers.Any(dir.MatchesMarker))
                    continue;

                var artifactNodes = new List<ScanNode>();
                foreach (var rel in eco.ArtifactDirs)
                {
                    var node = dir.ResolveRelative(rel);
                    if (node is not null)
                        artifactNodes.Add(node);
                }

                if (artifactNodes.Count == 0)
                    continue;

                var size = artifactNodes.Sum(n => n.Size);
                if (size <= 0)
                    continue;

                foreach (var n in artifactNodes)
                    skip.Add(n);

                var lastUsed = dir.NewestWriteTicks > 0 ? dir.NewestWrite : (DateTimeOffset?)null;
                var usage = new UsageSignal(lastUsed, "proje kaynakları son yazma", 0.6);
                var reason = lastUsed is { } used
                    ? $"Proje {dir.Name}, son değişiklik {Format.Ago(used, ctx.Now)}"
                    : $"Proje {dir.Name}, son değişiklik bilinmiyor";

                var unit = new Unit
                {
                    Id = UnitIdentity.Compute(UnitKind.DevArtifact, dir.FullPath),
                    Kind = UnitKind.DevArtifact,
                    Name = $"{dir.Name} ({eco.Name})",
                    Paths = artifactNodes.Select(n => n.FullPath).ToList(),
                    SizeBytes = size,
                    Usage = usage,
                    Confidence = 0.9,
                    Removal = RemovalMethod.DirectDelete,
                    Reason = reason,
                };
                units.Add(unit);
            }

            if (dir.Children is null)
                continue;
            foreach (var child in dir.Children)
                if (child.IsDirectory && !skip.Contains(child))
                    stack.Push(child);
        }

        return units;
    }
}
