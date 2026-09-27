using System.Runtime.InteropServices;

namespace DustyBytes.Scan;

internal static unsafe partial class Native
{
    public const uint FILE_ATTRIBUTE_READONLY = 0x1;
    public const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
    public const uint FILE_ATTRIBUTE_SYSTEM = 0x4;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_SPARSE_FILE = 0x200;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    public const uint FILE_ATTRIBUTE_COMPRESSED = 0x800;
    public const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
    public const uint CloudMask = FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_OFFLINE;

    public const int FindExInfoBasic = 1;
    public const int FindExSearchNameMatch = 0;
    public const uint FIND_FIRST_EX_LARGE_FETCH = 2;

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_HANDLE_EOF = 38;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
    public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
    public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_READ_ATTRIBUTES = 0x80;
    public const uint FILE_SHARE_ALL = 0x7;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    public const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    public const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
    public const uint FSCTL_READ_UNPRIVILEGED_USN_JOURNAL = 0x000903AB;

    public static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
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

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public ulong IdLow;
        public ulong IdHigh;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct WIN32_FILE_ATTRIBUTE_DATA
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V1
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_DESCRIPTOR
    {
        public uint dwSize;
        public int Type;
        public long FileId;
        public long Pad;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindFirstFileEx(string lpFileName, int fInfoLevelId, WIN32_FIND_DATAW* lpFindFileData, int fSearchOp, nint lpSearchFilter, uint dwAdditionalFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindNextFile(nint hFindFile, WIN32_FIND_DATAW* lpFindFileData);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(nint hFindFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetCompressedFileSize(string lpFileName, out uint lpFileSizeHigh);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpace(string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandle(nint hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandleEx(nint hFile, int FileInformationClass, void* lpFileInformation, uint dwBufferSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, out WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(nint hDevice, uint dwIoControlCode, void* lpInBuffer, uint nInBufferSize, void* lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, nint lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(nint hFile, void* lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, nint lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFilePointerEx(nint hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenFileById(nint hVolumeHint, ref FILE_ID_DESCRIPTOR lpFileId, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwFlagsAndAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static partial uint GetFinalPathNameByHandle(nint hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);

    public static long FileTimeToTicks(long ft) =>
        ft <= 0 ? 0 : ft + 504911232000000000L;
}
