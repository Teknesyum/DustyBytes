namespace DustyBytes.Signals.Tests;

internal static class Fixture
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    public static string Text(string name) => File.ReadAllText(Path(name));

    public static string TempDir()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dustybytes-signals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
