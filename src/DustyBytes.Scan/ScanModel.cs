namespace DustyBytes.Scan;

[Flags]
public enum NodeFlags : byte
{
    None = 0,
    ReparsePoint = 1,
    CloudPlaceholder = 2,
    HardLinkDuplicate = 4,
    Inaccessible = 8,
    Compressed = 16,
    Sparse = 32,
    Hidden = 64,
    System = 128,
}

public sealed class ScanNode
{
    public required string Name { get; init; }
    public ScanNode? Parent { get; set; }
    public List<ScanNode>? Children { get; set; }
    public bool IsDirectory { get; init; }
    public long Size { get; set; }
    public long LogicalSize { get; set; }
    public long CloudSize { get; set; }
    public long LastWriteTicks { get; set; }
    public long NewestWriteTicks { get; set; }
    public int FileCount { get; set; }
    public NodeFlags Flags { get; set; }
    public string? ReparseTag { get; set; }

    public DateTimeOffset LastWrite => new(LastWriteTicks, TimeSpan.Zero);
    public DateTimeOffset NewestWrite => new(NewestWriteTicks, TimeSpan.Zero);

    public string FullPath
    {
        get
        {
            if (Parent is null)
                return Name;
            var stack = new Stack<string>();
            for (var n = this; n is not null; n = n.Parent)
                stack.Push(n.Name);
            var root = stack.Pop();
            return Path.Combine(root, string.Join('\\', stack));
        }
    }

    public ScanNode? Child(string name) =>
        Children?.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ScanNode> Descendants()
    {
        if (Children is null)
            yield break;
        var stack = new Stack<ScanNode>(Children);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            if (n.Children is { } kids)
                foreach (var k in kids)
                    stack.Push(k);
        }
    }

    public ScanNode? Find(string path)
    {
        var rel = Path.GetRelativePath(FullPath, path);
        if (rel == ".")
            return this;
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            return null;
        var node = this;
        foreach (var part in rel.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Child(part);
            if (node is null)
                return null;
        }
        return node;
    }
}

public sealed record ScanProgress(string Step, long Files, long Directories, long Bytes, string? CurrentPath, double Percent);

public sealed class ScanResult
{
    public required ScanNode Root { get; init; }
    public long Files { get; init; }
    public long Directories { get; init; }
    public TimeSpan Elapsed { get; init; }
    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool Cancelled { get; init; }
    public string Method { get; init; } = "FindFirstFileEx";
    public UsnCursor? Usn { get; init; }
}

public enum ScanDecision
{
    Continue,
    Skip,
    Quit,
}

public sealed record ScanOptions
{
    public int MaxParallelism { get; init; } = Environment.ProcessorCount * 2;
    public IReadOnlyList<string> Excluded { get; init; } = [];
    public Func<string, ScanDecision>? Filter { get; init; }
    public bool SmallBuffer { get; init; }
    public IReadOnlyList<string> Priority { get; init; } = [];
    public Action<ScanNode>? SubtreeDone { get; init; }
}

public interface IScanner
{
    Task<ScanResult> ScanAsync(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel);
}
