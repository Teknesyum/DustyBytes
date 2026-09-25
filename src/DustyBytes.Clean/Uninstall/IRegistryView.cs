namespace DustyBytes.Clean.Uninstall;

public enum RegHive
{
    LocalMachine,
    CurrentUser,
    Users,
}

public enum RegView
{
    Registry64,
    Registry32,
}

public enum RegKind
{
    None,
    String,
    ExpandString,
    MultiString,
    DWord,
    QWord,
    Binary,
    Unknown,
}

public sealed record RegKeyRef(RegHive Hive, RegView View, string Path)
{
    public RegKeyRef Child(string name) => this with { Path = Path.Length == 0 ? name : Path + "\\" + name };

    public RegKeyRef? Parent()
    {
        var i = Path.LastIndexOf('\\');
        return i <= 0 ? null : this with { Path = Path[..i] };
    }

    public string Name => Path[(Path.LastIndexOf('\\') + 1)..];

    public string HiveName => Hive switch
    {
        RegHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegHive.CurrentUser => "HKEY_CURRENT_USER",
        _ => "HKEY_USERS",
    };

    public string ShortHive => Hive switch
    {
        RegHive.LocalMachine => "HKLM",
        RegHive.CurrentUser => "HKCU",
        _ => "HKU",
    };

    public string FullPath => HiveName + "\\" + Path;

    public string PhysicalPath =>
        HiveName + "\\" + (Hive == RegHive.LocalMachine && View == RegView.Registry32
            && Path.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)
            && !Path.StartsWith(@"SOFTWARE\WOW6432Node", StringComparison.OrdinalIgnoreCase)
            ? @"SOFTWARE\WOW6432Node\" + Path[9..]
            : Path);

    public string Display => ShortHive + "\\" + Path + (View == RegView.Registry32 && Hive == RegHive.LocalMachine ? " (32-bit)" : "");

    public string Identity => $"{ShortHive}{(View == RegView.Registry32 ? "32" : "64")}\\{Path}".ToLowerInvariant();
}

public sealed record RegValue(string Name, RegKind Kind, object? Data)
{
    public string? AsString => Data switch
    {
        string s => s,
        string[] m => string.Join("\n", m),
        int i => i.ToString(),
        long l => l.ToString(),
        _ => null,
    };

    public long? AsNumber => Data switch
    {
        int i => (uint)i,
        long l => l,
        string s when long.TryParse(s, out var n) => n,
        _ => null,
    };
}

public interface IRegistryView
{
    bool KeyExists(RegKeyRef key);
    IReadOnlyList<string> GetSubKeyNames(RegKeyRef key);
    IReadOnlyList<string> GetValueNames(RegKeyRef key);
    RegValue? GetValue(RegKeyRef key, string name);
    bool DeleteKeyTree(RegKeyRef key);
    bool DeleteValue(RegKeyRef key, string name);
}

public static class RegistryViewExtensions
{
    public static string? GetString(this IRegistryView reg, RegKeyRef key, string name)
    {
        var s = reg.GetValue(key, name)?.AsString;
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public static long? GetNumber(this IRegistryView reg, RegKeyRef key, string name) =>
        reg.GetValue(key, name)?.AsNumber;

    public static bool ValueExists(this IRegistryView reg, RegKeyRef key, string name) =>
        reg.GetValue(key, name) is not null;
}
