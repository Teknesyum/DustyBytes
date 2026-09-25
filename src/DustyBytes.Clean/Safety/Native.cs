using System.Runtime.InteropServices;

namespace DustyBytes.Clean.Safety;

internal static unsafe partial class Native
{
    public const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    public const uint MOVEFILE_WRITE_THROUGH = 0x8;

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_NOT_SAME_DEVICE = 17;
    public const int ERROR_SHARING_VIOLATION = 32;
    public const int ERROR_LOCK_VIOLATION = 33;
    public const int ERROR_ALREADY_EXISTS = 183;
    public const int ERROR_FILE_EXISTS = 80;
    public const int ERROR_MORE_DATA = 234;

    public const uint DRIVE_REMOTE = 4;
    public const uint FILE_PERSISTENT_ACLS = 0x8;
    public const uint FILE_READ_ONLY_VOLUME = 0x80000;

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string existing, string? newName, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathName(string fileName, char* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(string root, char* name, uint nameLength, out uint serial, out uint maxComponent, out uint flags, char* fsName, uint fsNameLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetDriveType(string root);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    public const int CCH_RM_SESSION_KEY = 32;
    public const int CCH_RM_MAX_APP_NAME = 255;
    public const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_PROCESS_INFO
    {
        public uint ProcessId;
        public uint StartTimeLow;
        public uint StartTimeHigh;
        public fixed char AppName[CCH_RM_MAX_APP_NAME + 1];
        public fixed char ServiceShortName[CCH_RM_MAX_SVC_NAME + 1];
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int Restartable;
    }

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmStartSession(out uint session, uint flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmRegisterResources(uint session, uint files, char** fileNames, uint applications, nint rgApplications, uint services, nint serviceNames);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmGetList(uint session, out uint needed, ref uint count, RM_PROCESS_INFO* infos, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmEndSession(uint session);
}
