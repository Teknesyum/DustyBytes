using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DustyBytes.Signals;

public sealed record UserAssistEntry(int RunCount, int FocusCount, TimeSpan FocusTime, DateTimeOffset? LastRun);

public static partial class UserAssistParser
{
    public static readonly Guid ExecutableGuid = new("CEBFF5CD-ACE2-4F4F-9178-9926F41749EA");
    public static readonly Guid ShortcutGuid = new("F4E57C4B-2036-45F0-A9AB-443BCFE33D9F");

    public static string Rot13(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
                >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
                _ => c,
            });
        }
        return sb.ToString();
    }

    public static UserAssistEntry? ParseData(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 68)
        {
            var runCount = BinaryPrimitives.ReadInt32LittleEndian(d[4..]);
            var focusCount = BinaryPrimitives.ReadInt32LittleEndian(d[8..]);
            var focusMs = BinaryPrimitives.ReadUInt32LittleEndian(d[12..]);
            var ft = BinaryPrimitives.ReadInt64LittleEndian(d[60..]);
            return new UserAssistEntry(runCount, focusCount, TimeSpan.FromMilliseconds(focusMs), FromFileTime(ft));
        }
        if (d.Length >= 16)
        {
            var runCount = BinaryPrimitives.ReadInt32LittleEndian(d[4..]);
            if (runCount >= 5)
                runCount -= 5;
            var ft = BinaryPrimitives.ReadInt64LittleEndian(d[8..]);
            return new UserAssistEntry(runCount, 0, TimeSpan.Zero, FromFileTime(ft));
        }
        return null;
    }

    public static DateTimeOffset? FromFileTime(long ft)
    {
        if (ft <= 0)
            return null;
        try
        {
            var t = DateTimeOffset.FromFileTime(ft).ToUniversalTime();
            return t.Year < 1970 ? null : t;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static string? ResolvePath(string decodedName, Func<Guid, string?> knownFolder)
    {
        var name = decodedName.Trim();
        if (name.StartsWith('{'))
        {
            var close = name.IndexOf('}');
            if (close < 0 || !Guid.TryParse(name.AsSpan(0, close + 1), out var guid))
                return null;
            var folder = knownFolder(guid);
            if (folder is null)
                return null;
            var rest = name[(close + 1)..].TrimStart('\\');
            return rest.Length == 0 ? folder : Path.Combine(folder, rest);
        }
        if (name.Length >= 3 && char.IsAsciiLetter(name[0]) && name[1] == ':' && name[2] == '\\')
            return name;
        return null;
    }

    public static unsafe string? KnownFolderPath(Guid id)
    {
        char* path = null;
        try
        {
            if (SHGetKnownFolderPath(&id, 0x00004000, 0, &path) != 0 || path == null)
                return null;
            return new string(path);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (path != null)
                Marshal.FreeCoTaskMem((nint)path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHGetKnownFolderPath(Guid* rfid, uint dwFlags, nint hToken, char** ppszPath);
}

public sealed class UserAssistSignal : IExecutableSignal
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

    private readonly Func<Guid, string?> _knownFolder;
    private readonly Dictionary<Guid, string?> _cache = [];

    public UserAssistSignal(Func<Guid, string?>? knownFolder = null)
    {
        _knownFolder = knownFolder ?? UserAssistParser.KnownFolderPath;
    }

    public string Source => SourceNames.UserAssist;
    public double Reliability => Signals.Reliability.UserAssist;

    public IReadOnlyList<ExecutableRun> Read()
    {
        using var root = Reg.Open(RegistryHive.CurrentUser, Root);
        if (root is null)
            return [];
        var runs = new Dictionary<string, ExecutableRun>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in root.GetSubKeyNames())
        {
            using var count = root.OpenSubKey(guid + @"\Count");
            if (count is null)
                continue;
            foreach (var valueName in count.GetValueNames())
            {
                if (count.GetValue(valueName) is not byte[] data)
                    continue;
                var entry = UserAssistParser.ParseData(data);
                if (entry?.LastRun is not { } last)
                    continue;
                var path = UserAssistParser.ResolvePath(UserAssistParser.Rot13(valueName), Resolve);
                if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                var run = new ExecutableRun(path, last, entry.RunCount, Source);
                if (!runs.TryGetValue(path, out var existing) || existing.LastRun < last)
                    runs[path] = run;
            }
        }
        return runs.Values.ToList();
    }

    private string? Resolve(Guid id)
    {
        if (!_cache.TryGetValue(id, out var path))
            _cache[id] = path = _knownFolder(id);
        return path;
    }
}
