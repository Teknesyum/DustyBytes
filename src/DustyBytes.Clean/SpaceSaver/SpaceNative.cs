using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DustyBytes.Clean.SpaceSaver;

internal static unsafe partial class SpaceNative
{
    public const uint FILE_READ_DATA = 0x1;
    public const uint FILE_READ_ATTRIBUTES = 0x80;
    public const uint FILE_WRITE_ATTRIBUTES = 0x100;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint FILE_SHARE_DELETE = 0x4;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FSCTL_DELETE_EXTERNAL_BACKING = 0x00090314;
    public const uint WOF_PROVIDER_FILE = 2;
    public const uint FILE_PROVIDER_COMPRESSION_XPRESS8K = 2;
    public const int ERROR_COMPRESSION_NOT_BENEFICIAL = 344;
    public const uint INVALID_FILE_SIZE = 0xFFFFFFFF;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int CF_PLACEHOLDER_INFO_STANDARD = 1;
    public const int CF_PIN_STATE_PINNED = 1;
    public const int CF_PIN_STATE_UNPINNED = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct WOF_FILE_COMPRESSION_INFO_V1
    {
        public uint Algorithm;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial uint GetCompressedFileSize(string name, out uint high);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint FindFirstFile(string name, WIN32_FIND_DATAW* data);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("wofutil.dll")]
    public static partial int WofSetFileDataLocation(SafeFileHandle file, uint provider, void* info, uint length);

    [LibraryImport("wofutil.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WofIsExternalFile(string path, out int isExternal, out uint provider, void* info, uint* length);

    [LibraryImport("cldapi.dll")]
    public static partial int CfGetPlaceholderInfo(SafeFileHandle file, int infoClass, void* info, uint length, out uint returned);

    [LibraryImport("cldapi.dll")]
    public static partial int CfSetPinState(SafeFileHandle file, int pinState, int flags, nint overlapped);
}
