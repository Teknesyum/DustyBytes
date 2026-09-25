using DustyBytes.Core;

namespace DustyBytes.Clean.Rules;

public static class PathGlob
{
    public static IEnumerable<string> ResolveDirectories(string pattern)
    {
        var expanded = Paths.Expand(pattern);
        var root = Path.GetPathRoot(expanded) ?? "";
        var rest = expanded[root.Length..];
        var segments = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return Expand(root.TrimEnd('\\'), segments, 0, isFile: false);
    }

    public static IEnumerable<string> ResolveFiles(string pattern)
    {
        var expanded = Paths.Expand(pattern);
        var root = Path.GetPathRoot(expanded) ?? "";
        var rest = expanded[root.Length..];
        var segments = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return Expand(root.TrimEnd('\\'), segments, 0, isFile: true);
    }

    static IEnumerable<string> Expand(string current, string[] segments, int index, bool isFile)
    {
        if (index == segments.Length)
        {
            if (isFile)
            {
                if (File.Exists(current))
                    yield return current;
            }
            else
            {
                yield return current;
            }
            yield break;
        }

        var segment = segments[index];
        var last = index == segments.Length - 1;

        if (!ContainsWildcard(segment))
        {
            var next = current.Length == 0 ? segment : Path.Combine(current, segment);
            foreach (var r in Expand(next, segments, index + 1, isFile))
                yield return r;
            yield break;
        }

        if (!Directory.Exists(current))
            yield break;

        IEnumerable<string> entries;
        try
        {
            entries = last && isFile
                ? Directory.EnumerateFiles(current, segment)
                : Directory.EnumerateDirectories(current, segment);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (IsReparsePoint(entry))
                continue;
            foreach (var r in Expand(entry, segments, index + 1, isFile))
                yield return r;
        }
    }

    static bool ContainsWildcard(string segment) => segment.Contains('*') || segment.Contains('?');

    static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
