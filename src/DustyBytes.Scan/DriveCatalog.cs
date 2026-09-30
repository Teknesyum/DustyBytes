using DustyBytes.Core;

namespace DustyBytes.Scan;

public enum DriveKind
{
    System,
    Fixed,
    Removable,
}

public sealed record DriveEntry(string Root, string Label, string Format, DriveKind Kind, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedShare => TotalBytes <= 0 ? 0 : Math.Clamp((double)UsedBytes / TotalBytes, 0, 1);
    public string Letter => Root.TrimEnd('\\');
}

public sealed record DriveProbe(string Root, DriveType Type, bool Ready, string Format, string Label, long TotalBytes, long FreeBytes, string? VolumeId);

public static class DriveCatalog
{
    public static readonly IReadOnlyList<string> Formats = ["NTFS", "exFAT", "ReFS"];

    public static string SystemRoot => Path.GetPathRoot(Environment.SystemDirectory)?.ToUpperInvariant() ?? @"C:\";

    public static IReadOnlyList<DriveEntry> List(bool includeRemovable) => Select(Probe(), includeRemovable, SystemRoot);

    public static IReadOnlyList<DriveEntry> Select(IEnumerable<DriveProbe> probes, bool includeRemovable, string systemRoot)
    {
        var system = Root(systemRoot);
        var volumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DriveEntry>();
        foreach (var p in probes.OrderBy(p => Root(p.Root).Equals(system, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(p => p.Root, StringComparer.OrdinalIgnoreCase))
        {
            var root = Root(p.Root);
            var isSystem = root.Equals(system, StringComparison.OrdinalIgnoreCase);
            if (!p.Ready || p.TotalBytes <= 0 || root.Length != 3 || root[1] != ':')
                continue;
            if (p.Type is not (DriveType.Fixed or DriveType.Removable) || p.Type == DriveType.Removable && !includeRemovable && !isSystem)
                continue;
            if (!Formats.Contains(p.Format, StringComparer.OrdinalIgnoreCase))
                continue;
            if (!isSystem && p.VolumeId is null)
                continue;
            if (p.VolumeId is { } id && !volumes.Add(id))
                continue;
            var kind = isSystem ? DriveKind.System : p.Type == DriveType.Removable ? DriveKind.Removable : DriveKind.Fixed;
            list.Add(new DriveEntry(root, p.Label, p.Format, kind, p.TotalBytes, Math.Clamp(p.FreeBytes, 0, p.TotalBytes)));
        }
        return list;
    }

    public static bool IsSupported(string root) => List(true).Any(d => d.Root.Equals(Root(root), StringComparison.OrdinalIgnoreCase));

    static string Root(string root)
    {
        var r = root.Replace('/', '\\').ToUpperInvariant();
        return r.EndsWith('\\') ? r : r + '\\';
    }

    static IEnumerable<DriveProbe> Probe()
    {
        foreach (var d in Drives())
        {
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable))
                continue;
            DriveProbe? probe;
            try
            {
                probe = d.IsReady
                    ? new DriveProbe(d.Name, d.DriveType, true, d.DriveFormat, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace, VolumeId(d.Name))
                    : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                probe = null;
            }
            if (probe is not null)
                yield return probe;
        }
    }

    static DriveInfo[] Drives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static unsafe string? VolumeId(string root)
    {
        var buffer = stackalloc char[64];
        return Native.GetVolumeNameForVolumeMountPoint(Paths.Normalize(root), buffer, 64) ? new string(buffer) : null;
    }
}
