using System.Diagnostics;

namespace DustyBytes.App.Services;

public static class Gezgin
{
    public static Func<string, bool> Launch { get; set; } = Start;

    public static string? Arguments(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"') || !Path.IsPathFullyQualified(path))
            return null;
        if (File.Exists(path) || Directory.Exists(path))
            return $"/select,\"{path}\"";
        var parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            parent = Path.GetDirectoryName(parent);
        return string.IsNullOrEmpty(parent) ? null : $"\"{parent}\"";
    }

    public static bool Reveal(string path) => Arguments(path) is { } arguments && Launch(arguments);

    static bool Start(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
            return process is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
