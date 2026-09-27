using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class InstallerExtractor : IUnitExtractor
{
    static readonly string[] Extensions = [".exe", ".msi", ".iso", ".zip", ".7z", ".rar"];
    static readonly string[] DownloadsPatterns = [@"Users\*\Downloads"];

    internal static IEnumerable<string> Roots => DownloadsPatterns;

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var pattern in DownloadsPatterns)
        {
            foreach (var downloads in PathPattern.Match(ctx.Root, pattern))
            {
                var files = downloads.Children?.Where(c => !c.IsDirectory) ?? [];
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file.Name);
                    if (!Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        continue;
                    if (file.Size <= 0)
                        continue;

                    var stem = Path.GetFileNameWithoutExtension(file.Name);
                    var siblingFolder = downloads.Children?.FirstOrDefault(c =>
                        c.IsDirectory && c.Name.Equals(stem, StringComparison.OrdinalIgnoreCase));
                    var confidence = siblingFolder is not null ? 0.9 : 0.5;

                    var usage = ctx.UsageIndex.ForFolder(file.FullPath);
                    if (!usage.IsKnown && file.LastWriteTicks > 0)
                        usage = new UsageSignal(file.LastWrite, "indirilen dosya son yazma", 0.3);

                    var unit = new Unit
                    {
                        Id = UnitIdentity.Compute(UnitKind.Installer, file.FullPath),
                        Kind = UnitKind.Installer,
                        Name = file.Name,
                        Paths = [file.FullPath],
                        SizeBytes = file.Size,
                        Usage = usage,
                        Confidence = confidence,
                        Removal = RemovalMethod.Quarantine,
                    };

                    var reason = siblingFolder is not null
                        ? $"{Scoring.Reason(unit, ctx.Now)}, açılmış kopyası var"
                        : Scoring.Reason(unit, ctx.Now);

                    units.Add(unit with { Reason = reason });
                }
            }
        }

        return units;
    }
}
