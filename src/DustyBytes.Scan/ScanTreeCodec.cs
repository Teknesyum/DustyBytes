namespace DustyBytes.Scan;

public static class ScanTreeCodec
{
    const uint Magic = 0x54424433;
    const int Version = 1;

    public static void Write(ScanResult result, string path)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            Write(result, stream);
        File.Move(temp, path, true);
    }

    public static void Write(ScanResult result, Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(result.Method);
        w.Write(result.FinishedAt.UtcTicks);
        w.Write(result.Elapsed.Ticks);
        w.Write(result.Cancelled);
        w.Write(result.Files);
        w.Write(result.Directories);
        w.Write(result.Usn is not null);
        if (result.Usn is { } usn)
        {
            w.Write(usn.JournalId);
            w.Write(usn.NextUsn);
        }
        w.Write7BitEncodedInt(result.Errors.Count);
        foreach (var e in result.Errors)
            w.Write(e);

        var stack = new Stack<ScanNode>();
        stack.Push(result.Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var kids = node.Children;
            var bits = (node.IsDirectory ? 1 : 0) | (node.NewestWriteTicks == node.LastWriteTicks ? 2 : 0) | (node.ReparseTag is null ? 0 : 4)
                | (node.LogicalSize == node.Size ? 8 : 0) | (node.CloudSize == 0 ? 16 : 0);
            w.Write(node.Name);
            w.Write((byte)bits);
            w.Write((byte)node.Flags);
            w.Write7BitEncodedInt64(node.Size);
            if ((bits & 8) == 0)
                w.Write7BitEncodedInt64(node.LogicalSize);
            if ((bits & 16) == 0)
                w.Write7BitEncodedInt64(node.CloudSize);
            w.Write(node.LastWriteTicks);
            if ((bits & 2) == 0)
                w.Write(node.NewestWriteTicks);
            if ((bits & 4) != 0)
                w.Write(node.ReparseTag!);
            if (node.IsDirectory)
            {
                w.Write7BitEncodedInt(node.FileCount);
                w.Write7BitEncodedInt(kids?.Count ?? 0);
            }
            if (kids is not null)
                for (var i = kids.Count - 1; i >= 0; i--)
                    stack.Push(kids[i]);
        }
    }

    public static ScanResult Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Read(stream);
    }

    public static ScanResult Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic || r.ReadInt32() != Version)
            throw new InvalidDataException("Tarama dosyası tanınmadı");
        var method = r.ReadString();
        var finished = new DateTimeOffset(r.ReadInt64(), TimeSpan.Zero);
        var elapsed = TimeSpan.FromTicks(r.ReadInt64());
        var cancelled = r.ReadBoolean();
        var files = r.ReadInt64();
        var dirs = r.ReadInt64();
        UsnCursor? usn = r.ReadBoolean() ? new UsnCursor(r.ReadUInt64(), r.ReadInt64()) : null;
        var errors = new string[r.Read7BitEncodedInt()];
        for (var i = 0; i < errors.Length; i++)
            errors[i] = r.ReadString();

        var root = ReadNode(r, null, out var rootKids);
        var stack = new Stack<(ScanNode Node, int Left)>();
        if (rootKids > 0)
            stack.Push((root, rootKids));
        while (stack.Count > 0)
        {
            var (parent, left) = stack.Pop();
            var node = ReadNode(r, parent, out var kids);
            parent.Children!.Add(node);
            if (left > 1)
                stack.Push((parent, left - 1));
            if (kids > 0)
                stack.Push((node, kids));
        }
        return new ScanResult
        {
            Root = root,
            Files = files,
            Directories = dirs,
            Method = method,
            FinishedAt = finished,
            Elapsed = elapsed,
            Cancelled = cancelled,
            Errors = errors,
            Usn = usn,
        };
    }

    static ScanNode ReadNode(BinaryReader r, ScanNode? parent, out int kids)
    {
        var name = r.ReadString();
        var bits = r.ReadByte();
        var flags = (NodeFlags)r.ReadByte();
        var size = r.Read7BitEncodedInt64();
        var logical = (bits & 8) != 0 ? size : r.Read7BitEncodedInt64();
        var cloud = (bits & 16) != 0 ? 0 : r.Read7BitEncodedInt64();
        var write = r.ReadInt64();
        var newest = (bits & 2) != 0 ? write : r.ReadInt64();
        var tag = (bits & 4) != 0 ? r.ReadString() : null;
        var isDir = (bits & 1) != 0;
        var fileCount = 0;
        kids = 0;
        if (isDir)
        {
            fileCount = r.Read7BitEncodedInt();
            kids = r.Read7BitEncodedInt();
        }
        return new ScanNode
        {
            Name = name,
            Parent = parent,
            IsDirectory = isDir,
            Size = size,
            LogicalSize = logical,
            CloudSize = cloud,
            LastWriteTicks = write,
            NewestWriteTicks = newest,
            FileCount = fileCount,
            Flags = flags,
            ReparseTag = tag,
            Children = kids > 0 ? new List<ScanNode>(kids) : null,
        };
    }
}
