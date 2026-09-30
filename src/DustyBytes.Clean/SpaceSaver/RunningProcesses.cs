using System.Diagnostics;
using DustyBytes.Core;

namespace DustyBytes.Clean.SpaceSaver;

public static class RunningProcesses
{
    public static List<string> Under(IEnumerable<string> roots, Func<IEnumerable<(int Id, string Name)>>? processes = null, Func<int, string?>? imagePath = null)
    {
        var list = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => Paths.Normalize(Paths.FromLong(r))).ToList();
        var found = new List<string>();
        if (list.Count == 0)
            return found;
        imagePath ??= ImagePath;
        foreach (var (id, name) in (processes ?? All)())
        {
            if (imagePath(id) is not { } image)
                continue;
            if (list.Any(root => Paths.IsUnder(image, root)) && !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                found.Add(name);
        }
        return found;
    }

    static IEnumerable<(int, string)> All()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
                yield return (p.Id, p.ProcessName);
        }
    }

    public static unsafe string? ImagePath(int id)
    {
        var handle = SpaceNative.OpenProcess(SpaceNative.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)id);
        if (handle == 0)
            return null;
        try
        {
            var buffer = stackalloc char[4096];
            uint size = 4096;
            return SpaceNative.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            SpaceNative.CloseHandle(handle);
        }
    }
}
