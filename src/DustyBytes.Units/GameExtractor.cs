using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class GameExtractor : IUnitExtractor
{
    const string SteamRedistributables = "228980";

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var game in ctx.UsageIndex.Games)
        {
            if (game is { Launcher: "Steam", Id: SteamRedistributables })
                continue;

            var node = ctx.Root.Find(game.InstallDir);
            var size = game.SizeOnDisk ?? node?.Size ?? 0;
            if (size <= 0)
                continue;

            var usage = game.LastPlayed is { } played
                ? new UsageSignal(played, $"{game.Launcher} son oynanma", 0.9)
                : UsageSignal.Unknown;

            var quarantine = game.Manifest is { Length: > 0 };
            var savesInside = game.SaveDirs.Any(d => Paths.IsUnder(d, game.InstallDir));
            var paths = new List<string> { game.InstallDir };
            if (quarantine)
                paths.Add(game.Manifest!);
            else
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
                Removal = quarantine ? RemovalMethod.Quarantine : RemovalMethod.Launcher,
                LauncherUri = game.UninstallUri,
                ContainsUserData = quarantine ? savesInside : game.SaveDirs.Count > 0,
                Effect = quarantine ? Effect(game.Launcher, savesInside) : "",
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }

    static string Effect(string launcher, bool savesInside) =>
        $"Oyun {launcher} kütüphanesinden kalkar. "
        + (savesInside ? "Kayıtlarınız oyun klasöründe; oyunla birlikte karantinaya girer. " : "Kayıtlarınız yerinde kalır. ")
        + "Geri alırsanız oyun olduğu gibi döner; 7 gün sonra kalıcı silinir, sonra isterseniz başlatıcıdan yeniden indirirsiniz.";
}
