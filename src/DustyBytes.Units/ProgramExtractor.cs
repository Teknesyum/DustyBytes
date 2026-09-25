using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed record ProgramInstall(string Id, string Name, string InstallLocation, string? Publisher = null);

public sealed class ProgramExtractor : IUnitExtractor
{
    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var program in ctx.Programs)
        {
            var raw = program.InstallLocation?.Trim().Trim('"') ?? "";
            if (raw.Length == 0 || !Path.IsPathFullyQualified(raw))
                continue;

            string dir;
            try
            {
                dir = Paths.Normalize(raw);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (dir.Length <= 3 || !seen.Add(dir))
                continue;

            var node = ctx.Root.Find(dir);
            if (node is not { IsDirectory: true } || node.Size <= 0)
                continue;

            var usage = ctx.UsageIndex.ForFolder(dir);
            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.Program, dir),
                Kind = UnitKind.Program,
                Name = program.Name,
                Paths = [dir],
                SizeBytes = node.Size,
                Usage = usage,
                Confidence = 0.8,
                Removal = RemovalMethod.Uninstaller,
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }
}
