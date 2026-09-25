using DustyBytes.Core;

namespace DustyBytes.Clean.Safety;

public static class DryRunLog
{
    static readonly object Gate = new();

    public static string FilePath { get; set; } = Path.Combine(Paths.AppData, "dryrun.log");

    public static void Write(string op, string path, string detail = "")
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{op}\t{path}\t{detail}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
