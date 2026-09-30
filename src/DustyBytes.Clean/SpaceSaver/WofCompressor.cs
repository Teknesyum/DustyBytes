using System.Runtime.InteropServices;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core;

namespace DustyBytes.Clean.SpaceSaver;

public enum WofOutcome
{
    Done,
    NotBeneficial,
    Busy,
    Failed,
}

public static class WofCompressor
{
    public static string? Unsupported(string path)
    {
        var volume = VolumeInfo.For(path);
        if (volume is null)
            return "Sürücü okunamadı";
        if (volume.Remote)
            return "Ağ sürücüsünde küçültme yapılmaz";
        if (volume.ReadOnly)
            return "Sürücü salt okunur";
        if (!volume.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            return $"Küçültme yalnız NTFS sürücülerde çalışır ({volume.FileSystem})";
        return null;
    }

    public static long AllocatedBytes(string path)
    {
        var low = SpaceNative.GetCompressedFileSize(Paths.ToLong(path), out var high);
        if (low == SpaceNative.INVALID_FILE_SIZE && Marshal.GetLastPInvokeError() != 0)
            return -1;
        return ((long)high << 32) | low;
    }

    public static unsafe bool IsCompressed(string path)
    {
        return SpaceNative.WofIsExternalFile(Paths.ToLong(path), out var external, out var provider, null, null) >= 0
            && external != 0
            && provider == SpaceNative.WOF_PROVIDER_FILE;
    }

    public static unsafe WofOutcome Compress(string path)
    {
        using var handle = SpaceNative.CreateFile(
            Paths.ToLong(path),
            SpaceNative.FILE_READ_DATA | SpaceNative.FILE_WRITE_ATTRIBUTES,
            SpaceNative.FILE_SHARE_READ,
            0, SpaceNative.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return Marshal.GetLastPInvokeError() is 32 or 33 ? WofOutcome.Busy : WofOutcome.Failed;
        var info = new SpaceNative.WOF_FILE_COMPRESSION_INFO_V1 { Algorithm = SpaceNative.FILE_PROVIDER_COMPRESSION_XPRESS8K };
        var hr = SpaceNative.WofSetFileDataLocation(handle, SpaceNative.WOF_PROVIDER_FILE, &info, (uint)sizeof(SpaceNative.WOF_FILE_COMPRESSION_INFO_V1));
        if (hr >= 0)
            return WofOutcome.Done;
        return (hr & 0xFFFF) == SpaceNative.ERROR_COMPRESSION_NOT_BENEFICIAL ? WofOutcome.NotBeneficial : WofOutcome.Failed;
    }

    public static unsafe WofOutcome Uncompress(string path)
    {
        using var handle = SpaceNative.CreateFile(
            Paths.ToLong(path),
            SpaceNative.FILE_READ_DATA | SpaceNative.FILE_WRITE_ATTRIBUTES,
            SpaceNative.FILE_SHARE_READ,
            0, SpaceNative.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return Marshal.GetLastPInvokeError() is 32 or 33 ? WofOutcome.Busy : WofOutcome.Failed;
        return SpaceNative.DeviceIoControl(handle, SpaceNative.FSCTL_DELETE_EXTERNAL_BACKING, null, 0, null, 0, out _, 0)
            ? WofOutcome.Done
            : WofOutcome.Failed;
    }

    public static long FreeBytes(string path) =>
        VolumeInfo.For(path) is { } v && Safety.Native.GetDiskFreeSpaceEx(v.Root, out var available, out _, out _) ? (long)available : 0;
}
