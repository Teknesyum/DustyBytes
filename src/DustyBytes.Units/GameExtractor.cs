using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class GameExtractor : IUnitExtractor
{
    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var game in ctx.UsageIndex.Games)
        {
            var node = ctx.Root.Find(game.InstallDir);
            var size = game.SizeOnDisk ?? node?.Size ?? 0;
            if (size <= 0)
                continue;

            var usage = game.LastPlayed is { } played
                ? new UsageSignal(played, $"{game.Launcher} son oynanma", 0.9)
                : UsageSignal.Unknown;

            var paths = new List<string> { game.InstallDir };
            paths.AddRange(game.SaveDirs);

            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.Game, game.InstallDir),
                Kind = UnitKind.Game,
                Name = game.Name,
                Paths = paths,
                SizeBytes = size,
                Usage = usage,
                Confidence = 1.0,
                Removal = RemovalMethod.Launcher,
                LauncherUri = game.UninstallUri,
                ContainsUserData = game.SaveDirs.Count > 0,
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }
}
