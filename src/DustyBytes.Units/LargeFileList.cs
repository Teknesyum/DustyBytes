using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed record LargeFileEntry(string Path, long SizeBytes, DateTimeOffset LastWrite);

public static class LargeFileList
{
    public static IReadOnlyList<LargeFileEntry> Build(ScanNode root, int take = 100) =>
        root.Descendants()
            .Where(n => !n.IsDirectory)
            .OrderByDescending(n => n.Size)
            .Take(take)
            .Select(n => new LargeFileEntry(n.FullPath, n.Size, n.LastWrite))
            .ToList();
}
