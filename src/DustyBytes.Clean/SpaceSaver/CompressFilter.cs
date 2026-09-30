namespace DustyBytes.Clean.SpaceSaver;

public static class CompressFilter
{
    public const long MinBytes = 64 * 1024;

    const FileAttributes SkipAttributes =
        FileAttributes.ReparsePoint | FileAttributes.Compressed | FileAttributes.Encrypted | FileAttributes.SparseFile |
        FileAttributes.Temporary | FileAttributes.Directory | CloudFiles.RecallMask;

    static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".flv", ".ts", ".m2ts",
        ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wma", ".wem", ".bnk",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".avif", ".jxl",
        ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".lz4", ".lzma", ".cab", ".br",
        ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".jar", ".apk", ".nupkg", ".whl",
        ".bik", ".bk2", ".usm",
        ".vhd", ".vhdx", ".vmdk", ".iso", ".wim", ".esd",
        ".log", ".tmp", ".db", ".sqlite", ".ldb", ".pdb", ".dmp",
    };

    static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "log", "cache", "caches", "saves", "save", "savegames", "crashes", "crashdumps", "temp", "tmp", "shadercache",
    };

    public static bool SkipsFolder(string name) => SkipFolders.Contains(name);

    public static bool Accepts(string path, long length, FileAttributes attributes) =>
        length >= MinBytes
        && (attributes & SkipAttributes) == 0
        && !SkipExtensions.Contains(Path.GetExtension(path));
}
