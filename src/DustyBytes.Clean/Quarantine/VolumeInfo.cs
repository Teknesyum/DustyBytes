using DustyBytes.Clean.Safety;
using DustyBytes.Core;

namespace DustyBytes.Clean.Quarantine;

public sealed record VolumeInfo(string Root, string Id, string FileSystem, bool Remote, bool ReadOnly, bool PersistentAcls)
{
    public bool SupportsQuarantine => !Remote && !ReadOnly && PersistentAcls;

    public long TotalBytes =>
        Native.GetDiskFreeSpaceEx(Root, out _, out var total, out _) ? (long)total : 0;

    public static unsafe VolumeInfo? For(string path)
    {
        var plain = Paths.FromLong(path);
        var buffer = stackalloc char[1024];
        if (!Native.GetVolumePathName(plain, buffer, 1024))
            return null;
        var root = new string(buffer);
        if (!root.EndsWith('\\'))
            root += '\\';

        var remote = Native.GetDriveType(root) == Native.DRIVE_REMOTE;
        var guid = stackalloc char[64];
        var id = Native.GetVolumeNameForVolumeMountPoint(root, guid, 64) ? new string(guid) : root.ToUpperInvariant();

        var fs = stackalloc char[64];
        var name = stackalloc char[128];
        uint flags = 0;
        var fsName = "";
        if (Native.GetVolumeInformation(root, name, 128, out _, out _, out flags, fs, 64))
            fsName = new string(fs);

        return new VolumeInfo(
            root,
            id,
            fsName,
            remote,
            (flags & Native.FILE_READ_ONLY_VOLUME) != 0,
            (flags & Native.FILE_PERSISTENT_ACLS) != 0);
    }
}
