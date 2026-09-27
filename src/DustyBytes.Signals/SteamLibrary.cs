using Microsoft.Win32;

namespace DustyBytes.Signals;

public sealed class SteamLibrary : IGameLibrary
{
    private readonly string? _steamRoot;

    public SteamLibrary(string? steamRoot = null)
    {
        _steamRoot = steamRoot is null ? FindSteamRoot() : Reg.NormalizeDir(steamRoot);
    }

    public string Launcher => "Steam";

    public string? SteamRoot => _steamRoot;

    public static string? FindSteamRoot() =>
        Reg.ExistingDir(Reg.String(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"))
        ?? Reg.ExistingDir(Reg.String(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"))
        ?? Reg.ExistingDir(Reg.String(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));

    public IReadOnlyList<string> LibraryRoots()
    {
        if (_steamRoot is null)
            return [];
        var roots = new List<string> { _steamRoot };
        foreach (var lib in LibraryFolders())
            if (!roots.Contains(lib, StringComparer.OrdinalIgnoreCase))
                roots.Add(lib);
        return roots;
    }

    public IReadOnlyList<string> LibraryFolders()
    {
        if (_steamRoot is null)
            return [];
        var vdf = Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf");
        var list = new List<string>();
        if (File.Exists(vdf))
            list.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        if (!list.Contains(_steamRoot, StringComparer.OrdinalIgnoreCase))
            list.Insert(0, _steamRoot);
        return list;
    }

    public static IReadOnlyList<string> ParseLibraryFolders(string text)
    {
        var root = Vdf.Parse(text);
        var list = new List<string>();
        foreach (var child in root.Children)
        {
            if (!int.TryParse(child.Name, out _))
                continue;
            var path = child.Value ?? child.Get("path");
            var dir = Reg.NormalizeDir(path);
            if (dir is not null && !list.Contains(dir, StringComparer.OrdinalIgnoreCase))
                list.Add(dir);
        }
        return list;
    }

    public IReadOnlyList<GameInstall> ReadInstalls()
    {
        var games = new List<GameInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in LibraryFolders())
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList();
            }
            catch
            {
                continue;
            }
            foreach (var file in files)
            {
                try
                {
                    var game = ParseAppManifest(File.ReadAllText(file), lib);
                    if (game is not null && seen.Add(game.Id))
                        games.Add(game with { Manifest = file });
                }
                catch
                {
                }
            }
        }
        return games;
    }

    public static GameInstall? ParseAppManifest(string text, string libraryFolder)
    {
        var app = Vdf.Parse(text);
        var id = app.Get("appid");
        var installDir = app.Get("installdir");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(installDir))
            return null;
        var lastPlayed = app.GetLong("LastPlayed");
        var size = app.GetLong("SizeOnDisk");
        return new GameInstall
        {
            Launcher = "Steam",
            Id = id,
            Name = app.Get("name") ?? installDir,
            InstallDir = Path.Combine(libraryFolder, "steamapps", "common", installDir),
            SizeOnDisk = size is > 0 ? size : null,
            LastPlayed = lastPlayed is > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed.Value) : null,
            UninstallUri = $"steam://uninstall/{id}",
        };
    }
}
