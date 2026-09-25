namespace DustyBytes.Core;

public static class Paths
{
    public const string LongPrefix = @"\\?\";
    public const string QuarantineDir = ".dustybytes";

    public static string ToLong(string path)
    {
        if (path.StartsWith(LongPrefix, StringComparison.Ordinal))
            return path;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return LongPrefix + "UNC" + full[1..];
        return LongPrefix + full;
    }

    public static string FromLong(string path)
    {
        if (path.StartsWith(LongPrefix + "UNC", StringComparison.OrdinalIgnoreCase))
            return @"\" + path[(LongPrefix.Length + 3)..];
        return path.StartsWith(LongPrefix, StringComparison.Ordinal) ? path[LongPrefix.Length..] : path;
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(FromLong(path).Replace('/', '\\'));
        return full.Length > 3 ? Path.TrimEndingDirectorySeparator(full) : full;
    }

    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (p.Equals(r, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = r.EndsWith('\\') ? r : r + '\\';
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string Expand(string path) =>
        Environment.ExpandEnvironmentVariables(path).Replace('/', '\\');

    public static string AppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DustyBytes");

    public static string RulesDir =>
        Path.Combine(AppContext.BaseDirectory, "rules");
}
