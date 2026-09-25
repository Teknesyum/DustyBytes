using System.IO.Enumeration;

namespace DustyBytes.Clean.Uninstall;

public static class FolderSize
{
    static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public static long Measure(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return -1;
            var total = 0L;
            var e = new FileSystemEnumerable<long>(folder, (ref FileSystemEntry entry) => entry.Length, Options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            };
            foreach (var len in e)
                total += len;
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    public static long MeasureAny(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        return Math.Max(0, Measure(path));
    }
}
