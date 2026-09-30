using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class OldDownloadsExtractor : IUnitExtractor
{
    public const string Title = "İndirilenler · eski";
    public static readonly TimeSpan Idle = TimeSpan.FromDays(90);

    static readonly string[] DownloadsPatterns = [@"Users\*\Downloads"];

    internal static IEnumerable<string> Roots => DownloadsPatterns;

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        foreach (var pattern in DownloadsPatterns)
        {
            foreach (var downloads in PathPattern.Match(ctx.Root, pattern))
            {
                var picked = new List<(string Path, long Size, DateTimeOffset Opened)>();
                foreach (var file in downloads.Children?.Where(c => !c.IsDirectory) ?? [])
                {
                    if (file.Size <= 0)
                        continue;
                    var opened = LastOpened(ctx, file.FullPath, file.LastWriteTicks > 0 ? file.LastWrite : null);
                    if (opened is not { } last || ctx.Now - last < Idle)
                        continue;
                    picked.Add((file.FullPath, file.Size, last));
                }
                if (picked.Count == 0)
                    continue;

                var newest = picked.Max(p => p.Opened);
                var unit = new Unit
                {
                    Id = UnitIdentity.Compute(UnitKind.OldDownload, downloads.FullPath),
                    Kind = UnitKind.OldDownload,
                    Name = Title,
                    Label = "Eski indirme",
                    Paths = [.. picked.Select(p => p.Path)],
                    SizeBytes = picked.Sum(p => p.Size),
                    Usage = new UsageSignal(newest, "indirilenlerde en son açılan", 0.5),
                    Confidence = 0.6,
                    Removal = RemovalMethod.Quarantine,
                    ContainsUserData = true,
                    Effect = Effect,
                };
                units.Add(unit with { Reason = ReasonFor(picked.Count) });
            }
        }
        return units;
    }

    public const string Effect =
        "Dosyalar karantinaya taşınır, kalıcı silinmez. Lazım olan varsa 7 gün içinde karantinadan geri alırsınız.";

    public static string ReasonFor(int count) => $"{count} dosya 90 günden uzun süredir açılmadı";

    public static DateTimeOffset? FileTimes(string path)
    {
        try
        {
            var info = new FileInfo(DustyBytes.Core.Paths.ToLong(path));
            if (!info.Exists)
                return null;
            var created = info.CreationTimeUtc;
            var accessed = info.LastAccessTimeUtc;
            return new DateTimeOffset(created > accessed ? created : accessed, TimeSpan.Zero);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static DateTimeOffset? LastOpened(UnitContext ctx, string path, DateTimeOffset? lastWrite)
    {
        DateTimeOffset? best = lastWrite;
        void Take(DateTimeOffset? value)
        {
            if (value is { } v && (best is null || v > best))
                best = v;
        }
        var usage = ctx.UsageIndex.ForFolder(path);
        if (usage.IsKnown)
            Take(usage.LastUsed);
        if (ctx.Opened is { } probe)
            Take(probe(path));
        return best;
    }
}
