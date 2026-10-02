using System.Collections.Frozen;
using System.Diagnostics;

namespace DustyBytes.App.Services;

public enum FileKind
{
    Video,
    Audio,
    Image,
    Other,
}

public static class SafeOpen
{
    public static readonly FrozenSet<string> Video = FrozenSet.ToFrozenSet(
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv"], StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> Audio = FrozenSet.ToFrozenSet(
        [".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"], StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> Image = FrozenSet.ToFrozenSet(
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff"], StringComparer.OrdinalIgnoreCase);

    public static FileKind KindOf(string path)
    {
        var ext = Path.GetExtension(path);
        if (Video.Contains(ext))
            return FileKind.Video;
        if (Audio.Contains(ext))
            return FileKind.Audio;
        return Image.Contains(ext) ? FileKind.Image : FileKind.Other;
    }

    public static bool Opens(FileKind kind) => kind is FileKind.Video or FileKind.Audio or FileKind.Image;

    static string? Shape(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Dosya yolu yok";
        if (path.IndexOfAny(['"', '\0', '*', '?', '<', '>', '|']) >= 0 || (path.Length > 2 && path.IndexOf(':', 2) >= 0))
            return "Dosya yolu geçersiz";
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return "Ağ yolları açılmaz";
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':')
            return "Dosya yolu tam değil";
        if (path.EndsWith('.') || path.EndsWith(' '))
            return "Dosya yolu geçersiz";
        return null;
    }

    public static string? Refuse(string? path)
    {
        if (Shape(path) is { } bad)
            return bad;
        if (!Opens(KindOf(path!)))
            return "Bu dosya türü güvenlik için açılmaz; yalnız video, ses ve resim açılır";
        if (!File.Exists(path))
            return "Dosya bulunamadı";
        return null;
    }

    public static string? Open(string? path)
    {
        if (Refuse(path) is { } reason)
            return reason;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path!) { UseShellExecute = true, Verb = "open" });
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return "Açılamadı: " + e.Message;
        }
    }

    static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static string? RefuseReveal(string? path)
    {
        if (Shape(path) is { } bad)
            return bad;
        return File.Exists(path) || Directory.Exists(path) ? null : "Dosya bulunamadı";
    }

    public static string? Reveal(string? path)
    {
        if (RefuseReveal(path) is { } reason)
            return reason;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Explorer, $"/select,\"{path}\"") { UseShellExecute = false });
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return "Klasör açılamadı: " + e.Message;
        }
    }
}
