using DustyBytes.Core;
using Microsoft.Win32.SafeHandles;

namespace DustyBytes.Scan.Duplicates;

public readonly record struct FileKey(uint Volume, ulong Index);

public enum FileState
{
    Ready,
    Missing,
    Cloud,
    Link,
    Unreadable,
}

public sealed record FileFacts(string Path, FileState State, FileKey Key, uint Links, long Length, DateTimeOffset Created, DateTimeOffset Written)
{
    public bool Ready => State == FileState.Ready;
}

public static class FileProbe
{
    const FileAttributes CloudAttributes = (FileAttributes)(Native.FILE_ATTRIBUTE_OFFLINE | Native.FILE_ATTRIBUTE_RECALL_ON_OPEN | Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS);

    public static bool IsCloud(FileAttributes attributes) => (attributes & CloudAttributes) != 0;

    public static FileFacts Probe(string path)
    {
        var none = new FileFacts(path, FileState.Missing, default, 0, 0, default, default);
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(Paths.ToLong(path));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return none;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return none with { State = FileState.Unreadable };
        }
        if ((attributes & FileAttributes.Directory) != 0)
            return none;
        if (IsCloud(attributes))
            return none with { State = FileState.Cloud };
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return none with { State = FileState.Link };
        try
        {
            using var stream = Open(path);
            return Describe(path, stream.SafeFileHandle);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return none;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return none with { State = FileState.Unreadable };
        }
    }

    public static FileStream Open(string path) =>
        new(Paths.ToLong(path), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan,
            BufferSize = 0,
        });

    public static FileFacts Describe(string path, SafeFileHandle handle)
    {
        if (!Native.GetFileInformationByHandle(handle.DangerousGetHandle(), out var info))
            return new FileFacts(path, FileState.Unreadable, default, 0, 0, default, default);
        var state = IsCloud((FileAttributes)info.dwFileAttributes) ? FileState.Cloud
            : (info.dwFileAttributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0 ? FileState.Link
            : FileState.Ready;
        return new FileFacts(
            path,
            state,
            new FileKey(info.dwVolumeSerialNumber, ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow),
            info.nNumberOfLinks,
            ((long)info.nFileSizeHigh << 32) | info.nFileSizeLow,
            new DateTimeOffset(Native.FileTimeToTicks(info.ftCreationTime), TimeSpan.Zero),
            new DateTimeOffset(Native.FileTimeToTicks(info.ftLastWriteTime), TimeSpan.Zero));
    }
}
