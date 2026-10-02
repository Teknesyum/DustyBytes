using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Protection;

namespace DustyBytes.Uninstall.Tests;

public sealed class FakeRegistryView : IRegistryView
{
    sealed class Node
    {
        public readonly Dictionary<string, RegValue> Values = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Children = [];
    }

    readonly Dictionary<string, Node> _keys = new(StringComparer.OrdinalIgnoreCase);

    static string Id(RegKeyRef k) => $"{k.Hive}|{k.View}|{k.Path.Trim('\\')}";

    public List<string> Deleted { get; } = [];

    public RegKeyRef Key(RegHive hive, RegView view, string path)
    {
        var key = new RegKeyRef(hive, view, path);
        Ensure(key);
        return key;
    }

    Node Ensure(RegKeyRef key)
    {
        if (_keys.TryGetValue(Id(key), out var n))
            return n;
        n = new Node();
        _keys[Id(key)] = n;
        if (key.Parent() is { } parent && parent.Path.Length > 0)
        {
            var p = Ensure(parent);
            if (!p.Children.Contains(key.Name, StringComparer.OrdinalIgnoreCase))
                p.Children.Add(key.Name);
        }
        return n;
    }

    public FakeRegistryView Set(RegKeyRef key, string name, object data, RegKind? kind = null)
    {
        var k = kind ?? data switch
        {
            string => RegKind.String,
            int => RegKind.DWord,
            long => RegKind.QWord,
            string[] => RegKind.MultiString,
            byte[] => RegKind.Binary,
            _ => RegKind.Unknown,
        };
        Ensure(key).Values[name] = new RegValue(name, k, data);
        return this;
    }

    public FakeRegistryView SetAll(RegKeyRef key, params (string Name, object Data)[] values)
    {
        foreach (var (n, d) in values)
            Set(key, n, d);
        return this;
    }

    public bool KeyExists(RegKeyRef key) => _keys.ContainsKey(Id(key));

    public IReadOnlyList<string> GetSubKeyNames(RegKeyRef key) =>
        _keys.TryGetValue(Id(key), out var n) ? n.Children.ToList() : [];

    public IReadOnlyList<string> GetValueNames(RegKeyRef key) =>
        _keys.TryGetValue(Id(key), out var n) ? n.Values.Keys.ToList() : [];

    public RegValue? GetValue(RegKeyRef key, string name) =>
        _keys.TryGetValue(Id(key), out var n) && n.Values.TryGetValue(name, out var v) ? v : null;

    public bool DeleteKeyTree(RegKeyRef key)
    {
        if (!_keys.TryGetValue(Id(key), out var n))
            return false;
        foreach (var child in n.Children.ToList())
            DeleteKeyTree(key.Child(child));
        _keys.Remove(Id(key));
        if (key.Parent() is { } parent && _keys.TryGetValue(Id(parent), out var p))
            p.Children.RemoveAll(c => c.Equals(key.Name, StringComparison.OrdinalIgnoreCase));
        Deleted.Add(key.Display);
        return true;
    }

    public bool DeleteValue(RegKeyRef key, string name)
    {
        if (!_keys.TryGetValue(Id(key), out var n) || !n.Values.Remove(name))
            return false;
        Deleted.Add(key.Display + "\\" + name);
        return true;
    }
}

public sealed class FakeProbe : IFileProbe
{
    public Dictionary<string, FileIdentity> Identities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Heads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Heads.ContainsKey(path) || File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileIdentity? Identity(string file) => Identities.GetValueOrDefault(file);

    public byte[]? ReadHead(string file, int max) => Heads.GetValueOrDefault(file);

    public Dictionary<string, string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? VersionText(string file) => Versions.GetValueOrDefault(file);
}

public sealed class FakeSystemActions : ISystemActions
{
    public List<string> Calls { get; } = [];

    public (bool Ok, string Message) DeleteService(string name)
    {
        Calls.Add("service:" + name);
        return (true, "ok");
    }

    public (bool Ok, string Message) DeleteScheduledTask(string taskPath)
    {
        Calls.Add("task:" + taskPath);
        return (true, "ok");
    }

    public (bool Ok, string Message) DeleteFirewallRule(RegKeyRef rulesKey, string ruleId, string ruleName, string? program)
    {
        Calls.Add("firewall:" + ruleId);
        return (true, "ok");
    }
}

public sealed class TempTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "DustyBytesUninstallTests", Guid.NewGuid().ToString("N"));

    public TempTree() => Directory.CreateDirectory(Root);

    public string Dir(string relative)
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "")
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public static class Fixture
{
    public const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static ProtectedList EmptyProtection(params string[] neverLeftover) =>
        new(new ProtectedRules { NeverLeftover = [.. neverLeftover] });

    public static RegKeyRef UninstallKey(FakeRegistryView reg, string keyName, RegHive hive = RegHive.LocalMachine, RegView view = RegView.Registry64) =>
        reg.Key(hive, view, Uninstall + "\\" + keyName);

    public static InstalledProgram Program(string name, string? location, string? publisher = null, RegKeyRef? key = null, string? uninstall = null) => new()
    {
        Id = key is null ? "reg:HKLM64:" + name : $"reg:HKLM64:{key.Name}",
        DisplayName = name,
        Source = ProgramSource.Registry,
        Publisher = publisher,
        InstallLocation = location,
        UninstallString = uninstall,
        Key = key,
        KeyName = key?.Name,
    };

    public static ScanContext Context(FakeRegistryView reg, IFileProbe probe, IReadOnlyList<InstalledProgram> programs, IReadOnlyList<string> bases, IReadOnlyList<string>? userData = null, DustyBytes.Core.Protection.ProtectedList? protection = null, IReadOnlyList<string>? settings = null) => new()
    {
        Registry = reg,
        Probe = probe,
        Protection = protection ?? EmptyProtection(),
        Programs = programs,
        UserDataRoots = userData ?? [],
        BroadRoots = bases,
        DataBases = bases,
        SettingsBases = settings ?? [],
    };
}
