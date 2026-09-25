using System.Runtime.InteropServices;
using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public static unsafe partial class BroadPaths
{
    static readonly Guid FolderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");
    static readonly Guid FolderDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    static readonly Guid FolderDocuments = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(Guid* rfid, uint dwFlags, nint hToken, char** ppszPath);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(void* pv);

    static readonly Lazy<string[]> _roots = new(BuildRoots);
    static readonly Lazy<string[]> _userFolders = new(BuildUserFolders);

    public static string? KnownFolder(Guid id)
    {
        char* p = null;
        try
        {
            if (SHGetKnownFolderPath(&id, 0, 0, &p) != 0 || p == null)
                return null;
            return new string(p);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (p != null)
                CoTaskMemFree(p);
        }
    }

    public static IReadOnlyList<string> UserDataFolders => _userFolders.Value;

    static string[] BuildUserFolders()
    {
        var list = new List<string?>
        {
            KnownFolder(FolderDownloads),
            KnownFolder(FolderDesktop),
            KnownFolder(FolderDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        return list.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Paths.Normalize(p!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string[] BuildRoots()
    {
        var sf = new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.StartMenu,
            Environment.SpecialFolder.CommonStartMenu,
            Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.Startup,
            Environment.SpecialFolder.CommonStartup,
            Environment.SpecialFolder.Templates,
            Environment.SpecialFolder.Fonts,
        };
        var list = sf.Select(Environment.GetFolderPath).Select(s => (string?)s).ToList();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0)
        {
            list.Add(Path.Combine(local, "Programs"));
            list.Add(Path.Combine(local, "Temp"));
            list.Add(Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow"));
        }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
            list.Add(Path.GetDirectoryName(profile));
        list.Add(Environment.GetEnvironmentVariable("PUBLIC"));
        return list.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Paths.Normalize(p!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsTooBroad(string path) => IsTooBroad(path, _roots.Value.Concat(_userFolders.Value));

    public static bool IsTooBroad(string path, IEnumerable<string> roots)
    {
        string p;
        try
        {
            p = Paths.Normalize(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
        if (p.Length <= 3)
            return true;
        foreach (var r in roots)
            if (Paths.IsUnder(r, p))
                return true;
        return false;
    }
}
