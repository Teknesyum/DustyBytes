using DustyBytes.Core;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.Uninstall;

public sealed class ScanContext
{
    public required IRegistryView Registry { get; init; }
    public required IFileProbe Probe { get; init; }
    public required ProtectedList Protection { get; init; }
    public IReadOnlyList<InstalledProgram> Programs { get; init; } = [];
    public IReadOnlyList<string> UserDataRoots { get; init; } = [];
    public IReadOnlyList<string> BroadRoots { get; init; } = [];
    public IReadOnlyList<string> DataBases { get; init; } = [];
    public IReadOnlyList<string> SettingsBases { get; init; } = [];
    public IReadOnlyList<string> ShortcutDirs { get; init; } = [];
    public IReadOnlyList<string> DesktopDirs { get; init; } = [];
    public string? TasksDir { get; init; }
    public bool ScanRegistrySoftware { get; init; } = true;
    public bool ScanServices { get; init; } = true;
    public bool ScanStartup { get; init; } = true;
    public bool ScanAssociations { get; init; } = true;
    public bool ScanFirewall { get; init; } = true;
    public string? UserSid { get; init; }
    public string? UserNote { get; init; }

    public RegKeyRef User(string path) => UserSid is { } sid
        ? new RegKeyRef(RegHive.Users, RegView.Registry64, sid + "\\" + path)
        : new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, path);

    public RegKeyRef UserClasses => UserSid is { } sid
        ? new RegKeyRef(RegHive.Users, RegView.Registry64, sid + "_Classes")
        : new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, @"Software\Classes");

    public static ScanContext ForSystem(ProtectedList protection, IReadOnlyList<InstalledProgram> programs, IRegistryView? registry = null, IFileProbe? probe = null, UserScope? user = null)
    {
        registry ??= WindowsRegistryView.Instance;
        var shell = new Dictionary<Environment.SpecialFolder, string>
        {
            [Environment.SpecialFolder.ApplicationData] = "AppData",
            [Environment.SpecialFolder.LocalApplicationData] = "Local AppData",
            [Environment.SpecialFolder.Programs] = "Programs",
            [Environment.SpecialFolder.Startup] = "Startup",
            [Environment.SpecialFolder.DesktopDirectory] = "Desktop",
        };
        string F(Environment.SpecialFolder f) =>
            (shell.TryGetValue(f, out var name) ? user?.Folder(registry, name) : null)
            ?? (f == Environment.SpecialFolder.UserProfile ? user?.ProfilePath : null)
            ?? Environment.GetFolderPath(f);
        var local = F(Environment.SpecialFolder.LocalApplicationData);
        var localLow = local.Length > 0 ? Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow") : "";
        var bases = new[]
        {
            F(Environment.SpecialFolder.ApplicationData),
            local,
            localLow,
            F(Environment.SpecialFolder.CommonApplicationData),
            F(Environment.SpecialFolder.ProgramFiles),
            F(Environment.SpecialFolder.ProgramFilesX86),
            local.Length > 0 ? Path.Combine(local, "Programs") : "",
        };
        var shortcuts = new[]
        {
            F(Environment.SpecialFolder.Programs),
            F(Environment.SpecialFolder.CommonPrograms),
            F(Environment.SpecialFolder.Startup),
            F(Environment.SpecialFolder.CommonStartup),
        };
        var desktops = new[]
        {
            F(Environment.SpecialFolder.DesktopDirectory),
            F(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var userData = BroadPaths.UserDataFolders.Concat(protection.NeverLeftoverRoots).ToList();
        return new ScanContext
        {
            Registry = registry,
            Probe = probe ?? FileProbe.Instance,
            Protection = protection,
            Programs = programs,
            UserDataRoots = Clean(userData),
            BroadRoots = Clean(bases.Concat(shortcuts).Concat(desktops).Concat(userData).Append(F(Environment.SpecialFolder.Windows)).Append(F(Environment.SpecialFolder.UserProfile))),
            DataBases = Clean(bases),
            SettingsBases = Clean([bases[0], bases[1], bases[2], bases[3]]),
            ShortcutDirs = Clean(shortcuts),
            DesktopDirs = Clean(desktops),
            TasksDir = ScheduledTasks.DefaultDirectory,
            UserSid = user?.Sid,
            UserNote = user?.Note,
        };
    }

    public static IReadOnlyList<string> Clean(IEnumerable<string?> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p) && !p!.Contains('%')).Select(p => Paths.Normalize(p!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public bool IsTooBroad(string path) =>
        BroadPaths.IsTooBroad(path, BroadRoots) || BroadPaths.IsTooBroad(path);

    public bool IsUserData(string path) =>
        UserDataRoots.Any(r => Paths.IsUnder(path, r) || Paths.IsUnder(r, path)) || Protection.IsNeverLeftover(path);
}
