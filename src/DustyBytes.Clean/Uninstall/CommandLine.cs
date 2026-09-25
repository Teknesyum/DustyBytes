using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Uninstall;

public static partial class CommandLine
{
    [GeneratedRegex(@"[A-Za-z]:\\[^""|<>*?\r\n]*?\.(exe|dll|sys|com|bat|cmd|ps1|msc|cpl|scr|ocx)(?=$|[\s"",;])", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedPath();

    public static string Clean(string raw)
    {
        var s = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (s.StartsWith(@"\??\", StringComparison.Ordinal))
            s = s[4..];
        if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            s = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), s[12..]);
        if (s.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            s = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), s);
        return s;
    }

    public static string? Executable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var s = Clean(command);
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : s[1..];
        }
        var m = UnquotedPath().Match(s);
        if (m.Success && m.Index == 0)
            return m.Value;
        var space = s.IndexOf(' ');
        return space > 0 ? s[..space] : s;
    }

    public static string Arguments(string command)
    {
        var s = Clean(command);
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 0 ? s[(end + 1)..].Trim() : "";
        }
        var exe = Executable(s) ?? "";
        return s.Length > exe.Length ? s[exe.Length..].Trim() : "";
    }

    public static IReadOnlyList<string> Paths(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return [];
        var s = Clean(command);
        var list = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var q = s.IndexOf('"', i);
            if (q < 0)
                break;
            var e = s.IndexOf('"', q + 1);
            if (e < 0)
                break;
            var inner = s[(q + 1)..e];
            if (inner.Length > 3 && inner[1] == ':' && inner[2] == '\\')
                list.Add(inner);
            i = e + 1;
        }
        foreach (Match m in UnquotedPath().Matches(s))
            if (!list.Any(p => p.Contains(m.Value, StringComparison.OrdinalIgnoreCase)))
                list.Add(m.Value);
        return list;
    }
}
