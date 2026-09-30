namespace DustyBytes.Scan.Duplicates;

public sealed record DuplicateCandidate(string Path, long Length, long Allocated);

public sealed record DuplicateCopy(string Path, long Allocated, DateTimeOffset Created, DateTimeOffset Written);

public sealed record DuplicateGroup(string Hash, long Length, IReadOnlyList<DuplicateCopy> Copies)
{
    public long Reclaimable => Copies.Count < 2 ? 0 : Copies.Sum(c => c.Allocated) - Copies.Max(c => c.Allocated);
}

public sealed record DuplicateProgress(string Step, double Percent, string? Line, int Groups, long Bytes);

public sealed class DuplicateFinder(HashCache? cache = null)
{
    public const long DefaultMinBytes = 10L * 1024 * 1024;
    const NodeFlags Skipped = NodeFlags.CloudPlaceholder | NodeFlags.ReparsePoint | NodeFlags.HardLinkDuplicate | NodeFlags.Inaccessible | NodeFlags.System;

    readonly HashCache _cache = cache ?? new HashCache();

    public int Opened { get; private set; }
    public int FullHashes { get; private set; }

    public static List<DuplicateCandidate> Candidates(IEnumerable<ScanNode> roots, long minBytes, Func<string, bool>? skipFolder = null, CancellationToken ct = default)
    {
        var list = new List<DuplicateCandidate>();
        var stack = new Stack<(ScanNode Node, string Path)>();
        foreach (var root in roots)
            stack.Push((root, root.Name));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, path) = stack.Pop();
            if (dir.Children is not { } kids)
                continue;
            foreach (var child in kids)
            {
                if ((child.Flags & Skipped) != 0)
                    continue;
                var full = Path.Combine(path, child.Name);
                if (child.IsDirectory)
                {
                    if (skipFolder?.Invoke(full) != true)
                        stack.Push((child, full));
                    continue;
                }
                if (child.LogicalSize >= minBytes && minBytes > 0)
                    list.Add(new DuplicateCandidate(full, child.LogicalSize, child.Size > 0 ? child.Size : child.LogicalSize));
            }
        }
        return list;
    }

    public Task<IReadOnlyList<DuplicateGroup>> FindAsync(IReadOnlyList<DuplicateCandidate> candidates, Action<DuplicateGroup>? found, IProgress<DuplicateProgress>? progress, CancellationToken ct) =>
        Task.Run(() => Find(candidates, found, progress, ct), ct);

    public IReadOnlyList<DuplicateGroup> Find(IReadOnlyList<DuplicateCandidate> candidates, Action<DuplicateGroup>? found, IProgress<DuplicateProgress>? progress, CancellationToken ct)
    {
        var bySize = candidates
            .GroupBy(c => c.Length)
            .Select(g => g.DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList())
            .Where(g => g.Count > 1)
            .OrderByDescending(g => g[0].Length * (g.Count - 1))
            .ToList();
        var groups = new List<DuplicateGroup>();
        long bytes = 0;
        var total = Math.Max(1.0, bySize.Sum(g => (double)g[0].Length * g.Count));
        double done = 0;
        for (var i = 0; i < bySize.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var set = bySize[i];
            progress?.Report(new DuplicateProgress("Kopyalar aranıyor", 100.0 * done / total, set[0].Path, groups.Count, bytes));
            done += (double)set[0].Length * set.Count;
            foreach (var group in Resolve(set, ct))
            {
                groups.Add(group);
                bytes += group.Reclaimable;
                found?.Invoke(group);
            }
        }
        progress?.Report(new DuplicateProgress("Kopya araması bitti", 100, null, groups.Count, bytes));
        return groups;
    }

    IEnumerable<DuplicateGroup> Resolve(List<DuplicateCandidate> set, CancellationToken ct)
    {
        var length = set[0].Length;
        var seen = new HashSet<FileKey>();
        var edges = new List<(DuplicateCandidate Candidate, FileFacts Facts, string Edge)>();
        foreach (var candidate in set)
        {
            ct.ThrowIfCancellationRequested();
            if (Edge(candidate, length) is not { } read || !seen.Add(read.Facts.Key))
                continue;
            edges.Add((candidate, read.Facts, read.Edge));
        }
        foreach (var byEdge in edges.GroupBy(e => e.Edge).Where(g => g.Count() > 1))
        {
            var full = new List<(DuplicateCandidate Candidate, FileFacts Facts, string Hash)>();
            foreach (var item in byEdge)
            {
                ct.ThrowIfCancellationRequested();
                if (Full(item.Facts, ct) is { } hash)
                    full.Add((item.Candidate, item.Facts, hash));
            }
            foreach (var same in full.GroupBy(f => f.Hash).Where(g => g.Count() > 1))
                yield return new DuplicateGroup(same.Key, length,
                    [.. same.Select(s => new DuplicateCopy(s.Candidate.Path, s.Candidate.Allocated, s.Facts.Created, s.Facts.Written))]);
        }
    }

    (FileFacts Facts, string Edge)? Edge(DuplicateCandidate candidate, long length)
    {
        var probe = FileProbe.Probe(candidate.Path);
        if (!probe.Ready)
            return null;
        try
        {
            using var stream = FileProbe.Open(candidate.Path);
            Opened++;
            var facts = FileProbe.Describe(candidate.Path, stream.SafeFileHandle);
            if (!facts.Ready || facts.Links > 1 || facts.Length != length)
                return null;
            if (_cache.Get(facts) is { Edge: { } cached })
                return (facts, cached);
            var edge = ContentHash.Edge(stream, length);
            _cache.Put(facts, edge, null);
            return (facts, edge);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    string? Full(FileFacts facts, CancellationToken ct)
    {
        if (_cache.Get(facts) is { Full: { } cached })
            return cached;
        try
        {
            using var stream = FileProbe.Open(facts.Path);
            var now = FileProbe.Describe(facts.Path, stream.SafeFileHandle);
            if (!now.Ready || now.Key != facts.Key || now.Length != facts.Length || now.Written != facts.Written)
                return null;
            FullHashes++;
            var hash = ContentHash.Full(stream, ct);
            _cache.Put(facts, null, hash);
            return hash;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
