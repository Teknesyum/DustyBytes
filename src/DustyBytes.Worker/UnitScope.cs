using DustyBytes.Core;
using DustyBytes.Core.Protection;

namespace DustyBytes.Worker;

public static class UnitScope
{
    public static Verdict? Refuse(string path, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Verdict.Deny("Yol boş", Badge.System);
        string target;
        try
        {
            var plain = Paths.FromLong(path);
            if (!Path.IsPathFullyQualified(plain) || plain.StartsWith(@"\\", StringComparison.Ordinal))
                return Verdict.Deny("Yol tam değil", Badge.System);
            target = Paths.Normalize(plain);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Verdict.Deny("Geçersiz yol", Badge.System);
        }
        if (target.IndexOf(':', 2) >= 0)
            return Verdict.Deny("Geçersiz yol", Badge.System);

        foreach (var raw in roots)
        {
            if (Root(raw) is not { } root || !Paths.IsUnder(target, root))
                continue;
            if (target.Equals(root, StringComparison.OrdinalIgnoreCase))
                return File.Exists(root) ? null : Verdict.Deny("Birimin kendisi değil, içindeki dosyalar seçilir", Badge.System);
            if (Directory.Exists(target))
                return Verdict.Deny("Yalnız tek tek dosyalar seçilir", Badge.System);
            if (ThroughLink(target, root))
                return Verdict.Deny("Yol bir bağlantı noktasının içinden geçiyor", Badge.Link);
            return null;
        }
        return Verdict.Deny("Yol birimin dışında", Badge.System);
    }

    static string? Root(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var plain = Paths.FromLong(raw);
            if (!Path.IsPathFullyQualified(plain) || plain.StartsWith(@"\\", StringComparison.Ordinal))
                return null;
            var root = Paths.Normalize(plain);
            if (root.IndexOf(':', 2) >= 0)
                return null;
            var drive = Path.GetPathRoot(root);
            return string.IsNullOrEmpty(drive) || root.Length <= drive.Length ? null : root;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    static bool ThroughLink(string target, string root)
    {
        var current = Path.GetDirectoryName(target);
        while (current is not null && current.Length > root.Length)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return true;
            }
            current = Path.GetDirectoryName(current);
        }
        return false;
    }
}
