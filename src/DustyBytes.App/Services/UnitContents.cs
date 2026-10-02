using System.Diagnostics;

namespace DustyBytes.App.Services;

public sealed record ContentFile(string Path, long SizeBytes, DateTime LastWrite, FileKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed record ContentsResult(IReadOnlyList<ContentFile> Files, long FileCount, long TotalBytes, bool Partial);

public static class UnitContents
{
    public const int Take = 30;
    public const int MaxDepth = 24;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static ContentsResult List(IEnumerable<string> roots, CancellationToken ct = default) =>
        List(roots, Take, MaxDepth, Budget, ct);

    public static ContentsResult List(IEnumerable<string> roots, int take, int maxDepth, TimeSpan budget, CancellationToken ct = default)
    {
        var heap = new PriorityQueue<ContentFile, long>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clock = Stopwatch.StartNew();
        long count = 0;
        long total = 0;
        var partial = false;

        void Add(FileInfo file)
        {
            if (!seen.Add(file.FullName))
                return;
            count++;
            total += file.Length;
            var entry = new ContentFile(file.FullName, file.Length, file.LastWriteTime, SafeOpen.KindOf(file.Name));
            if (heap.Count < take)
                heap.Enqueue(entry, entry.SizeBytes);
            else if (heap.TryPeek(out _, out var smallest) && entry.SizeBytes > smallest)
                heap.EnqueueDequeue(entry, entry.SizeBytes);
        }

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            try
            {
                if (File.Exists(root))
                {
                    Add(new FileInfo(root));
                    continue;
                }
                if (!Directory.Exists(root))
                    continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            var stack = new Stack<(DirectoryInfo Dir, int Depth)>();
            stack.Push((new DirectoryInfo(root), 0));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (clock.Elapsed > budget)
                {
                    partial = true;
                    break;
                }
                var (dir, depth) = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = dir.EnumerateFileSystemInfos("*", Options);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    continue;
                }
                try
                {
                    foreach (var entry in entries)
                    {
                        if (entry is FileInfo file)
                        {
                            Add(file);
                        }
                        else if (entry is DirectoryInfo sub)
                        {
                            if (IsLink(sub))
                                continue;
                            if (depth + 1 > maxDepth)
                            {
                                partial = true;
                                continue;
                            }
                            stack.Push((sub, depth + 1));
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                }
            }
            if (partial && clock.Elapsed > budget)
                break;
        }

        var files = new List<ContentFile>(heap.Count);
        while (heap.TryDequeue(out var file, out _))
            files.Add(file);
        files.Reverse();
        return new ContentsResult(files, count, total, partial);
    }

    static bool IsLink(DirectoryInfo dir)
    {
        try
        {
            return dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && dir.LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static string? MainVideo(IEnumerable<ContentFile> files) =>
        files.Where(f => f.Kind == FileKind.Video).MaxBy(f => f.SizeBytes)?.Path;
}
