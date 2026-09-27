namespace DustyBytes.Scan;

public static class ReparseTags
{
    public const uint MountPoint = 0xA0000003;
    public const uint Symlink = 0xA000000C;
    public const uint Dedup = 0x80000013;
    public const uint Wci = 0x80000018;
    public const uint AppExecLink = 0x8000001B;
    public const uint LxSymlink = 0xA000001D;
    public const uint AfUnix = 0x80000023;
    public const uint Wof = 0x80000017;
    public const uint ProjFs = 0x9000001C;

    public static bool IsNameSurrogate(uint tag) => (tag & 0x20000000) != 0;

    public static bool IsCloud(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;

    public static string Name(uint tag) => tag switch
    {
        MountPoint => "junction",
        Symlink => "symlink",
        Dedup => "dedup",
        Wci => "wci",
        AppExecLink => "appexeclink",
        LxSymlink => "lxsymlink",
        AfUnix => "afunix",
        Wof => "wof",
        ProjFs => "projfs",
        _ when IsCloud(tag) => "cloud",
        _ => "0x" + tag.ToString("X8"),
    };

    public static bool ShouldEnter(uint tag) => !IsNameSurrogate(tag);
}

public static class ScanTree
{
    public static void Aggregate(ScanNode root)
    {
        var stack = new Stack<(ScanNode Node, bool Visited)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            var (node, visited) = stack.Pop();
            if (!node.IsDirectory)
                continue;
            if (!visited)
            {
                stack.Push((node, true));
                if (node.Children is { } kids)
                    foreach (var k in kids)
                        if (k.IsDirectory)
                            stack.Push((k, false));
                continue;
            }
            long size = 0, logical = 0, cloud = 0, newest = 0;
            var count = 0;
            if (node.Children is { } children)
            {
                foreach (var c in children)
                {
                    if ((c.Flags & NodeFlags.HardLinkDuplicate) != 0)
                        continue;
                    size += c.Size;
                    logical += c.LogicalSize;
                    cloud += c.CloudSize;
                    if (c.IsDirectory)
                    {
                        count += c.FileCount;
                        if (c.NewestWriteTicks > newest)
                            newest = c.NewestWriteTicks;
                    }
                    else
                    {
                        count++;
                        if (c.LastWriteTicks > newest)
                            newest = c.LastWriteTicks;
                    }
                }
            }
            node.Size = size;
            node.LogicalSize = logical;
            node.CloudSize = cloud;
            node.FileCount = count;
            node.NewestWriteTicks = newest == 0 ? node.LastWriteTicks : newest;
        }
    }

    public static ScanNode Clone(ScanNode root)
    {
        static ScanNode Copy(ScanNode n, ScanNode? parent) => new()
        {
            Name = n.Name,
            Parent = parent,
            IsDirectory = n.IsDirectory,
            Size = n.Size,
            LogicalSize = n.LogicalSize,
            CloudSize = n.CloudSize,
            LastWriteTicks = n.LastWriteTicks,
            NewestWriteTicks = n.NewestWriteTicks,
            FileCount = n.FileCount,
            Flags = n.Flags,
            ReparseTag = n.ReparseTag,
        };
        var copy = Copy(root, null);
        var stack = new Stack<(ScanNode From, ScanNode To)>();
        stack.Push((root, copy));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            if (from.Children is not { Count: > 0 } kids)
                continue;
            var list = new List<ScanNode>(kids.Count);
            foreach (var k in kids)
            {
                var c = Copy(k, to);
                list.Add(c);
                if (k.Children is { Count: > 0 })
                    stack.Push((k, c));
            }
            to.Children = list;
        }
        return copy;
    }

    public static (long Files, long Directories) Count(ScanNode root)
    {
        long files = 0, dirs = 0;
        foreach (var n in root.Descendants())
        {
            if (n.IsDirectory)
                dirs++;
            else
                files++;
        }
        return (files, dirs);
    }
}
