using Microsoft.Win32;

namespace DustyBytes.Signals;

internal static class Reg
{
    public static string? String(RegistryHive hive, string key, string value, RegistryView view = RegistryView.Default)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var k = root.OpenSubKey(key);
            return k?.GetValue(value) is string s && s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public static RegistryKey? Open(RegistryHive hive, string key, RegistryView view = RegistryView.Default)
    {
        try
        {
            var root = RegistryKey.OpenBaseKey(hive, view);
            return root.OpenSubKey(key);
        }
        catch
        {
            return null;
        }
    }

    public static string? NormalizeDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(p))
                return null;
            p = Path.GetFullPath(p);
            return p.Length > 3 ? Path.TrimEndingDirectorySeparator(p) : p;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExistingDir(string? path)
    {
        var p = NormalizeDir(path);
        if (p is null)
            return null;
        if (Directory.Exists(p))
            return p;
        if (File.Exists(p))
            return Path.GetDirectoryName(p);
        return null;
    }
}
