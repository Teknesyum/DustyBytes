using Microsoft.Win32;

namespace DustyBytes.Signals;

public sealed class GogLibrary : IGameLibrary
{
    private const string GamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    private const string UninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public string Launcher => "GOG";

    public IReadOnlyList<string> LibraryRoots()
    {
        var roots = new List<string>();
        var client = Reg.ExistingDir(Reg.String(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client"));
        if (client is not null)
            roots.Add(client);
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
        using var games = Reg.Open(RegistryHive.LocalMachine, GamesKey);
        if (games is null)
            return [];
        var list = new List<GameInstall>();
        foreach (var id in games.GetSubKeyNames())
        {
            try
            {
                using var k = games.OpenSubKey(id);
                if (k is null)
                    continue;
                var dir = Reg.NormalizeDir(k.GetValue("path") as string);
                if (dir is null)
                    continue;
                var gameId = k.GetValue("gameID") as string ?? id;
                var exe = Reg.NormalizeDir(k.GetValue("exe") as string);
                list.Add(new GameInstall
                {
                    Launcher = "GOG",
                    Id = gameId,
                    Name = k.GetValue("gameName") as string ?? Path.GetFileName(dir),
                    InstallDir = dir,
                    UninstallUri = k.GetValue("uninstallCommand") as string
                        ?? Reg.String(RegistryHive.LocalMachine, $@"{UninstallKey}\{gameId}_is1", "UninstallString"),
                    Executables = exe is null ? [] : [exe],
                });
            }
            catch
            {
            }
        }
        return list;
    }
}
