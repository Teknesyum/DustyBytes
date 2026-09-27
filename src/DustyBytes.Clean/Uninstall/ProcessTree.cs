using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DustyBytes.Clean.Uninstall;

public sealed record ProcessRunResult(bool Started, bool Completed, int ExitCode, string Message, bool TimedOut = false);

public sealed record RunRequest(string CommandLine, string? WorkingDir, Action<int>? OnActive, TimeSpan? Timeout);

public static partial class ProcessTree
{
    const uint CREATE_SUSPENDED = 0x4;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
    const uint INFINITE = 0xFFFFFFFF;
    const int JobObjectBasicAccountingInformation = 1;

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct StartupInfo
    {
        public uint cb;
        public char* lpReserved;
        public char* lpDesktop;
        public char* lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public nint hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcess(char* app, char* cmd, nint pa, nint ta, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint env, char* cwd, StartupInfo* si, ProcessInformation* pi);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static unsafe partial nint CreateJobObject(nint attrs, char* name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryInformationJobObject(nint job, int cls, void* info, uint len, uint* ret);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(nint job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetExitCodeProcess(nint process, uint* code);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint ms);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    static unsafe nint NewJob() => CreateJobObject(0, null);

    static unsafe bool Start(string commandLine, string? workingDir, out ProcessInformation info)
    {
        var si = new StartupInfo { cb = (uint)sizeof(StartupInfo) };
        var pi = default(ProcessInformation);
        var buffer = (commandLine + "\0").ToCharArray();
        bool created;
        fixed (char* cmd = buffer)
        fixed (char* cwd = workingDir)
            created = CreateProcess(null, cmd, 0, 0, false, CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT, 0, workingDir is null ? null : cwd, &si, &pi);
        info = pi;
        return created;
    }

    static unsafe int ActiveProcesses(nint job)
    {
        JobAccounting acc;
        uint ret;
        return QueryInformationJobObject(job, JobObjectBasicAccountingInformation, &acc, (uint)sizeof(JobAccounting), &ret) ? (int)acc.ActiveProcesses : 0;
    }

    static unsafe int ExitCode(nint process)
    {
        uint code;
        return GetExitCodeProcess(process, &code) ? unchecked((int)code) : -1;
    }

    public static Task<ProcessRunResult> Run(RunRequest request, CancellationToken ct) =>
        RunAndWaitTree(request.CommandLine, request.WorkingDir, request.OnActive, request.Timeout, ct);

    public static async Task<ProcessRunResult> RunAndWaitTree(string commandLine, string? workingDir, Action<int>? onActive, TimeSpan? timeout, CancellationToken ct)
    {
        var job = NewJob();
        if (job == 0)
            return new(false, false, -1, $"İş nesnesi oluşturulamadı: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        ProcessInformation pi = default;
        try
        {
            if (!Start(commandLine, workingDir, out pi))
                return new(false, false, -1, $"Kaldırıcı başlatılamadı: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            var inJob = AssignProcessToJobObject(job, pi.hProcess);
            ResumeThread(pi.hThread);
            var deadline = timeout is { } t ? DateTime.UtcNow + t : DateTime.MaxValue;

            while (true)
            {
                if (ct.IsCancellationRequested)
                    return new(true, false, -1, "Bekleme iptal edildi; kaldırıcı çalışmaya devam ediyor olabilir");
                if (DateTime.UtcNow >= deadline)
                {
                    _ = inJob ? TerminateJobObject(job, 1) : TerminateProcess(pi.hProcess, 1);
                    return new(true, false, -1, "Sessiz kaldırıcı süresinde bitmedi; durduruldu", TimedOut: true);
                }
                var active = inJob ? ActiveProcesses(job) : WaitForSingleObject(pi.hProcess, 0) == 0 ? 0 : 1;
                onActive?.Invoke(active);
                if (active == 0)
                    break;
                try
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            WaitForSingleObject(pi.hProcess, INFINITE);
            var code = ExitCode(pi.hProcess);
            return new(true, true, code, inJob ? "Kaldırıcı ve alt süreçleri bitti" : "Kaldırıcı bitti (alt süreçler izlenemedi)");
        }
        finally
        {
            if (pi.hThread != 0)
                CloseHandle(pi.hThread);
            if (pi.hProcess != 0)
                CloseHandle(pi.hProcess);
            CloseHandle(job);
        }
    }
}
