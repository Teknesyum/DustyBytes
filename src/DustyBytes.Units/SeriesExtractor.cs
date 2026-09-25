using System.Text.RegularExpressions;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed partial class SeriesExtractor : IUnitExtractor
{
    static readonly string[] VideoExt = [".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts"];

    [GeneratedRegex(@"S(\d{1,2})E(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SxxEyyRegex();

    [GeneratedRegex(@"(?<!\d)(\d{1,2})x(\d{2,3})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex NxNRegex();

    [GeneratedRegex(@"^(Season|Sezon)\s*0*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var groups = new Dictionary<ScanNode, List<int>>();

        foreach (var file in ctx.Root.Descendants())
        {
            if (file.IsDirectory)
                continue;
            if (!VideoExt.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
                continue;

            var season = TryMatchSeason(file.Name);
            if (season is null)
                continue;

            var seriesRoot = SeriesRootFor(file.Parent);
            if (seriesRoot is null)
                continue;

            if (!groups.TryGetValue(seriesRoot, out var seasons))
            {
                seasons = [];
                groups[seriesRoot] = seasons;
            }
            seasons.Add(season.Value);
        }

        var units = new List<Unit>();
        foreach (var (root, seasons) in groups)
        {
            if (root.Size <= 0)
                continue;

            var distinctSeasons = seasons.Distinct().OrderBy(s => s).ToList();
            var usage = ctx.UsageIndex.ForMedia(root.FullPath);

            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.Series, root.FullPath),
                Kind = UnitKind.Series,
                Name = root.Name,
                Paths = [root.FullPath],
                SizeBytes = root.Size,
                Usage = usage,
                Confidence = 0.85,
                Removal = RemovalMethod.Quarantine,
            };

            var seasonText = distinctSeasons.Count > 0
                ? $", sezon {string.Join(", ", distinctSeasons)}"
                : "";
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) + seasonText });
        }

        return units;
    }

    static int? TryMatchSeason(string fileName)
    {
        var m = SxxEyyRegex().Match(fileName);
        if (m.Success)
            return int.Parse(m.Groups[1].Value);

        m = NxNRegex().Match(fileName);
        if (m.Success)
            return int.Parse(m.Groups[1].Value);

        return null;
    }

    static ScanNode? SeriesRootFor(ScanNode? parent)
    {
        if (parent is null)
            return null;
        if (SeasonFolderRegex().IsMatch(parent.Name) && parent.Parent is not null)
            return parent.Parent;
        return parent;
    }
}
