using DustyBytes.Clean.Quarantine;

namespace DustyBytes.App.Services;

public sealed record DriveSpace(string Root, long TotalBytes, long FreeBytes)
{
    public double FreeShare => TotalBytes <= 0 ? 1 : Math.Clamp((double)FreeBytes / TotalBytes, 0, 1);
    public int UsedPercent => (int)Math.Floor((1 - FreeShare) * 100);
    public string Letter => Root.TrimEnd('\\');
}

public sealed record FreeNowOffer(DriveSpace Drive, long Bytes, IReadOnlyList<string> Ids);

public interface INotifier
{
    bool Show(string title, string body, string launch);
}

public static class DiskCheck
{
    public const double LowShare = 0.10;
    public const long LowBytes = 15L << 30;
    public const string Body = "DustyBytes ile yer açın";

    public static bool IsLow(DriveSpace drive) =>
        drive.TotalBytes > 0 && (drive.FreeShare < LowShare || drive.FreeBytes < Math.Min(LowBytes, drive.TotalBytes / 4));

    public static bool IsCritical(DriveSpace drive) => drive.TotalBytes > 0 && drive.FreeShare < LowShare;

    public static IReadOnlyList<DriveSpace> Low(IEnumerable<DriveSpace> drives) =>
        [.. drives.Where(IsLow).OrderBy(d => d.FreeShare)];

    public static string Title(IReadOnlyList<DriveSpace> low) =>
        string.Join(", ", low.Select(d => $"{d.Letter} %{d.UsedPercent} dolu"));

    public static string Message(IReadOnlyList<DriveSpace> low) => Title(low) + " · " + Body;

    public static int Run(IEnumerable<DriveSpace> drives, INotifier notifier, Func<string, string?>? growth = null)
    {
        var low = Low(drives);
        if (low.Count == 0)
            return 0;
        var sentence = growth is null ? null : low.Select(d => growth(d.Root)).FirstOrDefault(s => s is { Length: > 0 });
        return notifier.Show(Title(low), sentence is null ? Body : sentence + ". " + Body, LaunchArgs.OpenUri) ? 0 : 1;
    }

    public static string? RootOf(QuarantineEntry entry)
    {
        foreach (var path in new[] { entry.Root, entry.OriginalPath })
        {
            if (string.IsNullOrEmpty(path))
                continue;
            try
            {
                if (Path.GetPathRoot(path) is { Length: > 0 } root)
                    return root;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    public static FreeNowOffer? FreeNow(IEnumerable<DriveSpace> drives, IEnumerable<QuarantineEntry> entries)
    {
        var byRoot = entries
            .Select(e => (Entry: e, Root: RootOf(e)))
            .Where(p => p.Root is not null)
            .GroupBy(p => p.Root!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Entry).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var drive in drives.Where(IsCritical).OrderBy(d => d.FreeShare))
        {
            if (!byRoot.TryGetValue(drive.Root, out var held))
                continue;
            var bytes = held.Sum(e => e.Size);
            if (bytes > 0)
                return new FreeNowOffer(drive, bytes, [.. held.Select(e => e.Id)]);
        }
        return null;
    }

    public static IReadOnlyList<DriveSpace> FixedDrives()
    {
        var found = new List<DriveSpace>();
        DriveInfo[] all;
        try
        {
            all = DriveInfo.GetDrives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return found;
        }
        foreach (var drive in all)
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady && drive.TotalSize > 0)
                    found.Add(new DriveSpace(drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return found;
    }

    public static DriveSpace? Measure(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            return drive.IsReady ? new DriveSpace(drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
