using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.App.Services;

public static class GrowthText
{
    public static string Signed(long bytes) => "+" + Format.Bytes(bytes);

    public static string Title(FolderGrowth growth) =>
        $"Son taramadan beri {Signed(growth.TotalBytes)} · en çok büyüyenler";

    public static string Weekly(FolderGrowth growth, DateTimeOffset now)
    {
        var days = Math.Max(1, (int)Math.Round((now - growth.Since).TotalDays));
        var lead = days is >= 5 and <= 10 ? "Geçen haftadan beri" : $"Son {days} günde";
        return $"{lead} {Signed(growth.TotalBytes)}: {string.Join(", ", growth.Top.Select(t => t.Name))}";
    }

    public static string? WeeklyFor(string root, DateTimeOffset now)
    {
        try
        {
            return new ScanIndex().WeeklyGrowth(root) is { } growth ? Weekly(growth, now) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return null;
        }
    }
}
