using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace DustyBytes.Worker;

internal static partial class WorkerNative
{
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_DUPLICATE = 0x0002;
    const uint TOKEN_QUERY = 0x0008;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    public static int? ClientProcessId(SafePipeHandle pipe) =>
        GetNamedPipeClientProcessId(pipe, out var pid) ? (int)pid : null;

    public static int? ServerProcessId(SafePipeHandle pipe) =>
        GetNamedPipeServerProcessId(pipe, out var pid) ? (int)pid : null;

    public static SecurityIdentifier? ProcessUser(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == 0)
            return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY | TOKEN_DUPLICATE, out var token))
                return null;
            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
