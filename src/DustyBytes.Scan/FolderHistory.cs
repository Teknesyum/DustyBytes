namespace DustyBytes.Scan;

public sealed record FolderSize(string Path, long Size);

public sealed record FolderGrowthItem(string Path, long Bytes)
{
    public string Name => FolderHistory.NameOf(Path);
}

public sealed record FolderGrowth(long TotalBytes, IReadOnlyList<FolderGrowthItem> Top, DateTimeOffset Since);

public sealed record FolderBaseline(DateTimeOffset At, IReadOnlyList<FolderSize> Folders);

public static class FolderHistory
{
    public const int MaxDepth = 3;
    public const long MinFolderBytes = 100L << 20;
    public const long MinGrowthBytes = 100L << 20;
    public const int MaxRowsPerSnapshot = 1000;
    public const int KeepDays = 30;
    public const long MaxStoreBytes = 5L << 20;
    public const int TopCount = 3;
    public const double Dominance = 0.6;

    public static IReadOnlyList<FolderSize> Extract(ScanResult result)
    {
        var found = new List<FolderSize>();
        var stack = new Stack<(ScanNode Node, string Path, int Depth)>();
        stack.Push((result.Root, result.Root.Name, 0));
        while (stack.Count > 0)
        {
            var (node, path, depth) = stack.Pop();
            if (node.Children is not { } kids)
                continue;
            foreach (var kid in kids)
            {
                if (!kid.IsDirectory || kid.Size < MinFolderBytes || kid.Flags.HasFlag(NodeFlags.ReparsePoint))
                    continue;
                var full = path.TrimEnd('\\') + "\\" + kid.Name;
                found.Add(new FolderSize(full, kid.Size));
                if (depth + 1 < MaxDepth)
                    stack.Push((kid, full, depth + 1));
            }
        }
        if (found.Count > MaxRowsPerSnapshot)
            found = [.. found.OrderByDescending(f => f.Size).Take(MaxRowsPerSnapshot)];
        return found;
    }

    public static FolderGrowth? Compare(IReadOnlyList<FolderSize> now, FolderBaseline? before)
    {
        if (before is null)
            return null;
        var old = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in before.Folders)
            old[f.Path] = f.Size;
        var delta = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in now)
            delta[f.Path] = f.Size - old.GetValueOrDefault(f.Path);
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var tops = new List<string>();
        foreach (var path in delta.Keys)
        {
            var parent = ParentOf(path);
            if (parent is not null && delta.ContainsKey(parent))
            {
                if (!children.TryGetValue(parent, out var list))
                    children[parent] = list = [];
                list.Add(path);
            }
            else
                tops.Add(path);
        }
        var items = new List<FolderGrowthItem>();
        long total = 0;
        foreach (var top in tops)
        {
            var d = delta[top];
            if (d < MinGrowthBytes)
                continue;
            total += d;
            var pick = top;
            while (children.TryGetValue(pick, out var kids))
            {
                var best = kids.MaxBy(k => delta[k])!;
                if (delta[best] < MinGrowthBytes || delta[best] < delta[pick] * Dominance)
                    break;
                pick = best;
            }
            items.Add(new FolderGrowthItem(pick, delta[pick]));
        }
        if (items.Count == 0)
            return null;
        return new FolderGrowth(total, [.. items.OrderByDescending(i => i.Bytes).Take(TopCount)], before.At);
    }

    public static FolderGrowth? Merge(IEnumerable<FolderGrowth?> parts)
    {
        var list = parts.OfType<FolderGrowth>().ToList();
        if (list.Count == 0)
            return null;
        return new FolderGrowth(list.Sum(p => p.TotalBytes),
            [.. list.SelectMany(p => p.Top).OrderByDescending(i => i.Bytes).Take(TopCount)],
            list.Min(p => p.Since));
    }

    public static string? ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var cut = trimmed.LastIndexOf('\\');
        return cut <= 2 ? null : trimmed[..cut];
    }

    public static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var cut = trimmed.LastIndexOf('\\');
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }
}
