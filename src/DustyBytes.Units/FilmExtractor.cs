using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class FilmExtractor : IUnitExtractor
{
    static readonly string[] VideoExt = [".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts"];
    static readonly string[] SubtitleExt = [".srt", ".sub", ".ass", ".vtt"];
    static readonly string[] PosterExt = [".jpg", ".jpeg", ".png"];
    const long MinFilmBytes = 700L * 1024 * 1024;

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var dir in AllDirs(ctx.Root))
        {
            var files = dir.Children?.Where(c => !c.IsDirectory).ToList() ?? [];
            var bigVideos = files.Where(f =>
                VideoExt.Contains(Path.GetExtension(f.Name), StringComparer.OrdinalIgnoreCase) &&
                f.Size >= MinFilmBytes).ToList();

            if (bigVideos.Count != 1)
                continue;

            var video = bigVideos[0];
            var companions = files.Where(f => f != video && IsCompanion(f.Name)).ToList();

            var size = video.Size + companions.Sum(c => c.Size);
            var usage = ctx.UsageIndex.ForMedia(video.FullPath);

            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.Film, dir.FullPath),
                Kind = UnitKind.Film,
                Name = dir.Name,
                Paths = [dir.FullPath],
                SizeBytes = size,
                Usage = usage,
                Confidence = companions.Count > 0 ? 0.85 : 0.6,
                Removal = RemovalMethod.Quarantine,
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }

    static bool IsCompanion(string name) =>
        SubtitleExt.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ||
        PosterExt.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ||
        name.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<ScanNode> AllDirs(ScanNode root)
    {
        yield return root;
        foreach (var d in root.Descendants())
            if (d.IsDirectory)
                yield return d;
    }
}
