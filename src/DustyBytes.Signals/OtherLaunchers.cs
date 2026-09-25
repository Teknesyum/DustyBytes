using Microsoft.Win32;

namespace DustyBytes.Signals;

public sealed class PresenceLibrary : IGameLibrary
{
    private readonly (RegistryHive Hive, string Key, string Value)[] _probes;
    private readonly string[] _fallbackDirs;

    private PresenceLibrary(string launcher, (RegistryHive, string, string)[] probes, string[] fallbackDirs)
    {
        Launcher = launcher;
        _probes = probes;
        _fallbackDirs = fallbackDirs;
    }

    public string Launcher { get; }

    public static PresenceLibrary BattleNet() => new("Battle.net",
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", "InstallLocation"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net\Capabilities", "ApplicationIcon"),
    ],
    [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net")]);

    public static PresenceLibrary Ea() => new("EA",
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Electronic Arts\EA Desktop", "InstallLocation"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Electronic Arts\EA Desktop", "InstallLocation"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Origin", "ClientPath"),
    ],
    [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Electronic Arts", "EA Desktop")]);

    public IReadOnlyList<string> LibraryRoots()
    {
        foreach (var (hive, key, value) in _probes)
        {
            var raw = Reg.String(hive, key, value);
            if (raw is not null && raw.Contains(','))
                raw = raw[..raw.LastIndexOf(',')];
            var dir = Reg.ExistingDir(raw);
            if (dir is not null)
                return [dir];
        }
        foreach (var dir in _fallbackDirs)
            if (Directory.Exists(dir))
                return [dir];
        return [];
    }

    public IReadOnlyList<GameInstall> ReadInstalls() => [];
}

public sealed class UbisoftLibrary : IGameLibrary
{
    private const string LauncherKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher";

    public string Launcher => "Ubisoft";

    public IReadOnlyList<string> LibraryRoots()
    {
        var roots = new List<string>();
        var dir = Reg.ExistingDir(Reg.String(RegistryHive.LocalMachine, LauncherKey, "InstallDir"));
        if (dir is not null)
            roots.Add(dir);
        foreach (var game in ReadInstalls())
        {
            var parent = Path.GetDirectoryName(game.InstallDir);
            if (parent is not null && Path.GetPathRoot(parent) != parent && !roots.Contains(parent, StringComparer.OrdinalIgnoreCase))
                roots.Add(parent);
        }
        return roots;
    }

    public IReadOnlyList<GameInstall> ReadInstalls()
    {
        using var installs = Reg.Open(RegistryHive.LocalMachine, LauncherKey + @"\Installs");
        if (installs is null)
            return [];
        var list = new List<GameInstall>();
        foreach (var id in installs.GetSubKeyNames())
        {
            using var k = installs.OpenSubKey(id);
            var dir = Reg.NormalizeDir(k?.GetValue("InstallDir") as string);
            if (dir is null)
                continue;
            list.Add(new GameInstall
            {
                Launcher = "Ubisoft",
                Id = id,
                Name = Path.GetFileName(dir),
                InstallDir = dir,
                UninstallUri = $"uplay://uninstall/{id}",
            });
        }
        return list;
    }
}
