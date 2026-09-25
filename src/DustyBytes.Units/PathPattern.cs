using DustyBytes.Scan;

namespace DustyBytes.Units;

internal static class PathPattern
{
    public static IEnumerable<ScanNode> Match(ScanNode root, string pattern)
    {
        IEnumerable<ScanNode> current = [root];
        foreach (var segment in pattern.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.SelectMany(n => n.Children ?? [])
                .Where(c => c.IsDirectory && (segment == "*" || c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)));
        }
        return current;
    }
}

internal static class ScanNodeExtensions
{
    public static ScanNode? ResolveRelative(this ScanNode node, string relative)
    {
        var current = node;
        foreach (var part in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            current = current?.Child(part);
            if (current is null)
                return null;
        }
        return current;
    }

    public static bool MatchesMarker(this ScanNode dir, string marker)
    {
        if (dir.Children is null)
            return false;

        if (!marker.StartsWith('*'))
            return dir.Children.Any(c => !c.IsDirectory && c.Name.Equals(marker, StringComparison.OrdinalIgnoreCase));

        var suffix = marker[1..];
        return dir.Children.Any(c => !c.IsDirectory && c.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
