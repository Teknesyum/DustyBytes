using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace DustyBytes.Signals;

public static class Ini
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        result[""] = current;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1].Trim();
                if (!result.TryGetValue(name, out current!))
                    result[name] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }
}

public sealed class VlcSignal : IMediaSignal
{
    private readonly string _iniPath;

    public VlcSignal(string? iniPath = null)
    {
        _iniPath = iniPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlc-qt-interface.ini");
    }

    public string Source => SourceNames.Vlc;

    public IReadOnlyList<MediaPlay> Read() =>
        File.Exists(_iniPath) ? Parse(File.ReadAllText(_iniPath)) : [];

    public static IReadOnlyList<MediaPlay> Parse(string iniText)
    {
        var ini = Ini.Parse(iniText);
        if (!ini.TryGetValue("RecentsMRL", out var section) || !section.TryGetValue("list", out var list))
            return [];
        var plays = new List<MediaPlay>();
        foreach (var item in SplitQtList(list))
        {
            var path = FileUriToPath(item);
            if (path is not null && !plays.Any(p => p.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                plays.Add(new MediaPlay(path, null, SourceNames.Vlc));
        }
        return plays;
    }

    public static IReadOnlyList<string> SplitQtList(string value)
    {
        var items = new List<string>();
        if (value.StartsWith("@Invalid", StringComparison.Ordinal))
            return items;
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted && c == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[++i]);
                continue;
            }
            if (!quoted && c == ',')
            {
                Add(items, sb);
                continue;
            }
            sb.Append(c);
        }
        Add(items, sb);
        return items;

        static void Add(List<string> items, StringBuilder sb)
        {
            var s = sb.ToString().Trim();
            if (s.Length > 0)
                items.Add(s);
            sb.Clear();
        }
    }

    public static string? FileUriToPath(string item)
    {
        if (item.Length >= 3 && char.IsAsciiLetter(item[0]) && item[1] == ':' && item[2] is '\\' or '/')
            return item.Replace('/', '\\');
        if (!item.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;
        return Uri.TryCreate(item, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
    }
}

public sealed class MpcHcSignal : IMediaSignal
{
    private const string RegRoot = @"Software\MPC-HC\MPC-HC";

    private readonly IReadOnlyList<string> _iniPaths;
    private readonly bool _useRegistry;

    public MpcHcSignal(IReadOnlyList<string>? iniPaths = null, bool useRegistry = true)
    {
        _iniPaths = iniPaths ?? DefaultIniPaths();
        _useRegistry = useRegistry;
    }

    public string Source => SourceNames.MpcHc;

    public static IReadOnlyList<string> DefaultIniPaths()
    {
        var list = new List<string>();
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MPC-HC");
        var dirs = new List<string> { appData };
        var exe = Reg.String(RegistryHive.CurrentUser, RegRoot, "ExePath");
        var exeDir = Reg.ExistingDir(exe);
        if (exeDir is not null)
            dirs.Add(exeDir);
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            dirs.Add(Path.Combine(Environment.GetFolderPath(pf), "MPC-HC"));
        foreach (var dir in dirs)
            foreach (var name in new[] { "mpc-hc64.ini", "mpc-hc.ini" })
                list.Add(Path.Combine(dir, name));
        return list;
    }

    public IReadOnlyList<MediaPlay> Read()
    {
        var plays = new Dictionary<string, MediaPlay>(StringComparer.OrdinalIgnoreCase);
        if (_useRegistry)
            foreach (var p in ReadRegistry())
                Merge(plays, p);
        foreach (var ini in _iniPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(ini))
                    foreach (var p in ParseIni(File.ReadAllText(ini)))
                        Merge(plays, p);
            }
            catch
            {
            }
        }
        return plays.Values.ToList();
    }

    public static IReadOnlyList<MediaPlay> ParseIni(string text)
    {
        var ini = Ini.Parse(text);
        var plays = new Dictionary<string, MediaPlay>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, section) in ini)
        {
            if (name.StartsWith(@"MediaHistory\", StringComparison.OrdinalIgnoreCase))
            {
                if (section.TryGetValue("Filename", out var file) && ToPath(file) is { } path)
                    Merge(plays, new MediaPlay(path, ParseLastOpened(section.GetValueOrDefault("LastOpened")), SourceNames.MpcHc));
            }
            else if (name.Equals("Recent File List", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (key, value) in section)
                    if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && ToPath(value) is { } path)
                        Merge(plays, new MediaPlay(path, null, SourceNames.MpcHc));
            }
        }
        return plays.Values.ToList();
    }

    public static DateTimeOffset? ParseLastOpened(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("0000", StringComparison.Ordinal))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t
            : null;
    }

    private static IEnumerable<MediaPlay> ReadRegistry()
    {
        var list = new List<MediaPlay>();
        using var history = Reg.Open(RegistryHive.CurrentUser, RegRoot + @"\MediaHistory");
        if (history is not null)
        {
            foreach (var hash in history.GetSubKeyNames())
            {
                using var k = history.OpenSubKey(hash);
                if (k?.GetValue("Filename") is string file && ToPath(file) is { } path)
                    list.Add(new MediaPlay(path, ParseLastOpened(k.GetValue("LastOpened") as string), SourceNames.MpcHc));
            }
        }
        using var recent = Reg.Open(RegistryHive.CurrentUser, RegRoot + @"\Recent File List");
        if (recent is not null)
            foreach (var name in recent.GetValueNames())
                if (recent.GetValue(name) is string file && ToPath(file) is { } path)
                    list.Add(new MediaPlay(path, null, SourceNames.MpcHc));
        return list;
    }

    private static string? ToPath(string value)
    {
        var v = value.Trim().Trim('"');
        return VlcSignal.FileUriToPath(v) ?? (v.StartsWith(@"\\", StringComparison.Ordinal) ? v : null);
    }

    private static void Merge(Dictionary<string, MediaPlay> plays, MediaPlay p)
    {
        if (!plays.TryGetValue(p.FilePath, out var existing) || Newer(p.LastPlayed, existing.LastPlayed))
            plays[p.FilePath] = p;
    }

    private static bool Newer(DateTimeOffset? a, DateTimeOffset? b) =>
        a is not null && (b is null || a > b);
}
