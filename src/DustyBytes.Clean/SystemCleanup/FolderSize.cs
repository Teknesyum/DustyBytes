namespace DustyBytes.Clean.SystemCleanup;

public static class FolderSize
{
    public static long Measure(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files;
            IEnumerable<string> dirs;
            try
            {
                files = Directory.EnumerateFiles(current);
                dirs = Directory.EnumerateDirectories(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var f in files)
            {
                try
                {
                    total += new FileInfo(f).Length;
                }
                catch (IOException)
                {
                }
            }

            foreach (var d in dirs)
            {
                try
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch (IOException)
                {
                    continue;
                }
                stack.Push(d);
            }
        }
        return total;
    }
}
