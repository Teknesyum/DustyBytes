using System.IO.Enumeration;
using DustyBytes.Core;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.SpaceSaver;

public sealed record PlannedFile(string Path, long Length);

public static class CompressPlan
{
    public static List<PlannedFile> Candidates(IEnumerable<string> roots, Func<string, Verdict> check, CancellationToken ct = default) =>
        Walk(roots, check, skipFolders: true, ct)
            .Where(f => CompressFilter.Accepts(f.Path, f.Length, f.Attributes))
            .Select(f => new PlannedFile(f.Path, f.Length))
            .ToList();

    public static List<PlannedFile> Compressed(IEnumerable<string> roots, Func<string, Verdict> check, Func<string, bool> isCompressed, CancellationToken ct = default) =>
        Walk(roots, check, skipFolders: false, ct)
            .Where(f => (f.Attributes & CloudFiles.RecallMask) == 0 && isCompressed(f.Path))
            .Select(f => new PlannedFile(f.Path, f.Length))
            .ToList();

    static IEnumerable<(string Path, long Length, FileAttributes Attributes)> Walk(IEnumerable<string> roots, Func<string, Verdict> check, bool skipFolders, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var root = Paths.Normalize(Paths.FromLong(raw));
            if (!seen.Add(root) || !Directory.Exists(root) || !check(root).Allowed)
                continue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            };
            var entries = new FileSystemEnumerable<(string, long, FileAttributes)>(
                root,
                (ref FileSystemEntry e) => (e.ToFullPath(), e.Length, e.Attributes),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    (e.Attributes & FileAttributes.ReparsePoint) == 0 && !(skipFolders && CompressFilter.SkipsFolder(e.FileName.ToString())),
            };
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (check(entry.Item1).Allowed)
                    yield return entry;
            }
        }
    }
}
