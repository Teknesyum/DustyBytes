using System.Diagnostics;

namespace DustyBytes.Safety.Tests;

public sealed class TempTree : IDisposable
{
    public string Root { get; }

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "DustyBytes.Safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string P(params string[] parts) => Path.Combine([Root, .. parts]);

    public string File(string relative, string content = "x", DateTime? stamp = null)
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        if (stamp is { } s)
        {
            System.IO.File.SetCreationTimeUtc(path, s);
            System.IO.File.SetLastWriteTimeUtc(path, s.AddHours(1));
            System.IO.File.SetLastAccessTimeUtc(path, s.AddHours(2));
        }
        return path;
    }

    public string Dir(string relative)
    {
        var path = P(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (!Directory.Exists(link))
            throw new InvalidOperationException("Junction oluşturulamadı: " + p.StandardError.ReadToEnd());
    }

    public void Dispose()
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories).ToList())
            {
                try
                {
                    var attrs = System.IO.File.GetAttributes(entry);
                    if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        System.IO.File.SetAttributes(entry, attrs & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        try
        {
            Directory.Delete(Root, true);
        }
        catch
        {
        }
    }
}
