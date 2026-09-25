using System.Text.Json;
using DustyBytes.Core;

namespace DustyBytes.App.Services;

public sealed class WindowStateStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(Paths.AppData, "window.json");

    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(Path))
                return null;
            var placement = JsonSerializer.Deserialize(File.ReadAllText(Path), AppJson.Default.WindowPlacement);
            return placement is { Width: > 0, Height: > 0 } ? placement : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(placement, AppJson.Default.WindowPlacement));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static bool IsVisible(WindowPlacement placement, IReadOnlyList<(double X, double Y, double Width, double Height)> screens)
    {
        const double grip = 48;
        foreach (var s in screens)
        {
            var left = Math.Max(placement.X, s.X);
            var top = Math.Max(placement.Y, s.Y);
            var right = Math.Min(placement.X + placement.Width, s.X + s.Width);
            var bottom = Math.Min(placement.Y + placement.Height, s.Y + s.Height);
            if (right - left >= grip && bottom - top >= grip && placement.Y >= s.Y && placement.Y < s.Y + s.Height)
                return true;
        }
        return false;
    }
}
