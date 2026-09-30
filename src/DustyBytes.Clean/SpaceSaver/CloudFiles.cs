using System.Buffers.Binary;
using DustyBytes.Core;

namespace DustyBytes.Clean.SpaceSaver;

public sealed record CloudState(bool InSync, int PinState, long OnDiskBytes, long ModifiedBytes);

public static class CloudFiles
{
    public const int MinAgeDays = 180;
    public const FileAttributes Pinned = (FileAttributes)0x80000;
    public const FileAttributes Unpinned = (FileAttributes)0x100000;
    public const FileAttributes RecallMask = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;
    const uint CloudTag = 0x9000001A;
    const uint CloudTagMask = 0xFFFF0FFF;
    const int PinUnspecified = 0;
    const int PinInherit = 4;

    public static bool IsCloudTag(uint? tag) => tag is { } t && (t & CloudTagMask) == CloudTag;

    public static bool IsOldEnough(DateTime lastWriteUtc, DateTime lastAccessUtc, DateTime nowUtc) =>
        (nowUtc - (lastWriteUtc > lastAccessUtc ? lastWriteUtc : lastAccessUtc)).TotalDays >= MinAgeDays;

    public static bool IsEligible(CloudState? state, long length, FileAttributes attributes) =>
        state is { InSync: true, ModifiedBytes: 0 } s
        && s.OnDiskBytes >= length
        && length > 0
        && s.PinState is PinUnspecified or PinInherit
        && (attributes & FileAttributes.ReparsePoint) != 0
        && (attributes & (RecallMask | Pinned | Unpinned | FileAttributes.Directory)) == 0;

    public static unsafe uint? ReparseTag(string path)
    {
        SpaceNative.WIN32_FIND_DATAW data;
        var handle = SpaceNative.FindFirstFile(Paths.ToLong(path), &data);
        if (handle == -1)
            return null;
        SpaceNative.FindClose(handle);
        return (data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : null;
    }

    public static unsafe CloudState? Probe(string path)
    {
        using var handle = SpaceNative.CreateFile(
            Paths.ToLong(path),
            SpaceNative.FILE_READ_ATTRIBUTES,
            SpaceNative.FILE_SHARE_READ | SpaceNative.FILE_SHARE_WRITE | SpaceNative.FILE_SHARE_DELETE,
            0, SpaceNative.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return null;
        var buffer = stackalloc byte[4096 + 64];
        var span = new ReadOnlySpan<byte>(buffer, 4096 + 64);
        if (SpaceNative.CfGetPlaceholderInfo(handle, SpaceNative.CF_PLACEHOLDER_INFO_STANDARD, buffer, 4096 + 64, out var returned) < 0 || returned < 40)
            return null;
        return new CloudState(
            BinaryPrimitives.ReadInt32LittleEndian(span[36..]) == 1,
            BinaryPrimitives.ReadInt32LittleEndian(span[32..]),
            BinaryPrimitives.ReadInt64LittleEndian(span),
            BinaryPrimitives.ReadInt64LittleEndian(span[16..]));
    }

    public static int SetPinState(string path, bool keepOnDevice)
    {
        using var handle = SpaceNative.CreateFile(
            Paths.ToLong(path),
            SpaceNative.FILE_READ_ATTRIBUTES | SpaceNative.FILE_WRITE_ATTRIBUTES,
            SpaceNative.FILE_SHARE_READ | SpaceNative.FILE_SHARE_WRITE | SpaceNative.FILE_SHARE_DELETE,
            0, SpaceNative.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        var hr = SpaceNative.CfSetPinState(handle, keepOnDevice ? SpaceNative.CF_PIN_STATE_PINNED : SpaceNative.CF_PIN_STATE_UNPINNED, 0, 0);
        return hr < 0 ? hr : 0;
    }

    public static long OnDiskBytes(string path) => WofCompressor.AllocatedBytes(path);
}
