using System.Text.Json;
using Microsoft.Win32;

namespace DustyBytes.Signals;

public sealed class EpicLibrary : IGameLibrary
{
    public const string LauncherUri = "com.epicgames.launcher://store/library";

    private readonly string _manifestDir;

    public EpicLibrary(string? manifestDir = null)
    {
        _manifestDir = manifestDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public string Launcher => "Epic";

    public IReadOnlyList<string> LibraryRoots()
    {
        var roots = new List<string>();
        var launcher = FindLauncherDir();
        if (launcher is not null)
            roots.Add(launcher);
        foreach (var game in ReadInstalls())
        {
            var parent = Path.GetDirectoryName(game.InstallDir);
            if (parent is not null && Path.GetPathRoot(parent) != parent && !roots.Contains(parent, StringComparer.OrdinalIgnoreCase))
                roots.Add(parent);
        }
        return roots;
    }

    public static string? FindLauncherDir()
    {
        var exe = Reg.String(RegistryHive.LocalMachine, @"SOFTWARE\Classes\com.epicgames.launcher\shell\open\command", "");
        if (exe is not null)
        {
            var path = exe.TrimStart('"');
            var end = path.IndexOf('"');
            if (end > 0)
                path = path[..end];
            var dir = Reg.ExistingDir(Path.GetDirectoryName(path));
            if (dir is not null)
            {
                var marker = dir.IndexOf(@"\Launcher\", StringComparison.OrdinalIgnoreCase);
                return marker > 0 ? dir[..marker] : dir;
            }
        }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public IReadOnlyList<GameInstall> ReadInstalls()
    {
        if (!Directory.Exists(_manifestDir))
            return [];
        var games = new List<GameInstall>();
        foreach (var file in Directory.EnumerateFiles(_manifestDir, "*.item"))
        {
            try
            {
                var game = ParseItem(File.ReadAllText(file));
                if (game is not null)
                    games.Add(game);
            }
            catch
            {
            }
        }
        return games;
    }

    public static GameInstall? ParseItem(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object)
            return null;
        if (Bool(r, "bIsIncompleteInstall") == true)
            return null;
        var main = Str(r, "MainGameAppName");
        var app = Str(r, "AppName");
        if (main is not null && app is not null && !main.Equals(app, StringComparison.OrdinalIgnoreCase))
            return null;
        var location = Reg.NormalizeDir(Str(r, "InstallLocation"));
        if (location is null || app is null)
            return null;
        var exe = Str(r, "LaunchExecutable");
        long? size = r.TryGetProperty("InstallSize", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var v) && v > 0 ? v : null;
        return new GameInstall
        {
            Launcher = "Epic",
            Id = app,
            Name = Str(r, "DisplayName") ?? app,
            InstallDir = location,
            SizeOnDisk = size,
            LastPlayed = null,
            UninstallUri = LauncherUri,
            Executables = string.IsNullOrEmpty(exe) ? [] : [Path.GetFullPath(Path.Combine(location, exe.Replace('/', '\\')))],
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s ? s : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
}
