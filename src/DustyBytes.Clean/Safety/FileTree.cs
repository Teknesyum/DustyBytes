using DustyBytes.Core;

namespace DustyBytes.Clean.Safety;

public sealed class TreeDeleteReport
{
    public long RemovedBytes { get; set; }
    public int RemovedCount { get; set; }
    public List<string> Locked { get; } = [];
    public List<string> Failed { get; } = [];
    public List<string> Protected { get; } = [];
    public List<string> Remaining { get; } = [];
    public bool Complete => Locked.Count == 0 && Failed.Count == 0 && Protected.Count == 0 && Remaining.Count == 0;
}

public static class FileTree
{
    static readonly EnumerationOptions Flat = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    static bool IsLink(FileAttributes a) => (a & FileAttributes.ReparsePoint) != 0;

    public static bool Exists(string path)
    {
        var l = Paths.ToLong(path);
        return File.Exists(l) || Directory.Exists(l);
    }

    public static (long Bytes, int Count) Measure(string path)
    {
        var root = Paths.ToLong(path);
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
        if ((attrs & FileAttributes.Directory) == 0)
            return (new FileInfo(root).Length, 1);
        if (IsLink(attrs))
            return (0, 1);

        long bytes = 0;
        var count = 1;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Flat).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var e in entries)
            {
                count++;
                if (e is FileInfo f)
                    bytes += IsLink(f.Attributes) ? 0 : f.Length;
                else if (!IsLink(e.Attributes))
                    stack.Push(e.FullName);
            }
        }
        return (bytes, count);
    }

    static bool IsLockError(Exception e) =>
        e is IOException io && ((io.HResult & 0xFFFF) is Native.ERROR_SHARING_VIOLATION or Native.ERROR_LOCK_VIOLATION);

    static void ClearReadOnly(string path, FileAttributes attrs)
    {
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
    }

    public static TreeDeleteReport Delete(string path, Func<string, string?>? protectedName = null)
    {
        var report = new TreeDeleteReport();
        var root = Paths.ToLong(path);
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(root);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return report;
        }

        if ((attrs & FileAttributes.Directory) == 0 || IsLink(attrs))
        {
            DeleteEntry(root, attrs, report);
            return report;
        }

        var order = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            order.Add(dir);
            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Flat).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                report.Failed.Add(Paths.FromLong(dir));
                continue;
            }
            foreach (var e in entries)
            {
                if (protectedName?.Invoke(e.Name) is not null)
                {
                    report.Protected.Add(Paths.FromLong(e.FullName));
                    continue;
                }
                if (e is DirectoryInfo && !IsLink(e.Attributes))
                    stack.Push(e.FullName);
                else
                    DeleteEntry(e.FullName, e.Attributes, report);
            }
        }

        for (var i = order.Count - 1; i >= 0; i--)
        {
            var dir = order[i];
            try
            {
                var a = File.GetAttributes(dir);
                ClearReadOnly(dir, a);
                Directory.Delete(dir, false);
                report.RemovedCount++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                report.Remaining.Add(Paths.FromLong(dir));
            }
        }
        return report;
    }

    static void DeleteEntry(string path, FileAttributes attrs, TreeDeleteReport report)
    {
        try
        {
            var isDir = (attrs & FileAttributes.Directory) != 0;
            var size = isDir || IsLink(attrs) ? 0 : new FileInfo(path).Length;
            ClearReadOnly(path, attrs);
            if (isDir)
                Directory.Delete(path, false);
            else
                File.Delete(path);
            report.RemovedBytes += size;
            report.RemovedCount++;
        }
        catch (Exception e) when (IsLockError(e))
        {
            report.Locked.Add(Paths.FromLong(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            report.Failed.Add(Paths.FromLong(path));
        }
    }
}
