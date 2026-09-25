using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DustyBytes.Clean.Uninstall;

public interface ISystemActions
{
    (bool Ok, string Message) DeleteService(string name);
    (bool Ok, string Message) DeleteScheduledTask(string taskPath);
    (bool Ok, string Message) DeleteFirewallRule(RegKeyRef rulesKey, string ruleId, string ruleName, string? program);
}

public sealed unsafe partial class SystemActions : ISystemActions
{
    public static readonly SystemActions Instance = new();

    const uint SC_MANAGER_CONNECT = 0x1;
    const uint DELETE = 0x10000;
    const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint scm, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint service);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    (bool Ok, string Message) ISystemActions.DeleteService(string name)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == 0)
            return (false, Win32Message("Servis yöneticisi açılamadı"));
        try
        {
            var svc = OpenService(scm, name, DELETE);
            if (svc == 0)
                return (false, Win32Message($"Servis açılamadı: {name}"));
            try
            {
                if (DeleteService(svc))
                    return (true, "Servis silinmek üzere işaretlendi; çalışıyorsa yeniden başlatmada gider");
                var err = Marshal.GetLastWin32Error();
                return err == ERROR_SERVICE_MARKED_FOR_DELETE ? (true, "Servis zaten silinmek üzere işaretli") : (false, $"Servis silinemedi: {new Win32Exception(err).Message}");
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public (bool Ok, string Message) DeleteScheduledTask(string taskPath)
    {
        if (taskPath.Contains('"'))
            return (false, "Geçersiz görev adı");
        return Run("schtasks.exe", ["/Delete", "/TN", taskPath, "/F"]);
    }

    public (bool Ok, string Message) DeleteFirewallRule(RegKeyRef rulesKey, string ruleId, string ruleName, string? program)
    {
        if (ruleName.Contains('"') || program?.Contains('"') == true)
            return (false, "Geçersiz kural adı");
        var args = new List<string> { "advfirewall", "firewall", "delete", "rule", $"name={ruleName}" };
        if (program is not null)
            args.Add($"program={program}");
        return Run("netsh.exe", args);
    }

    static (bool, string) Run(string exe, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            return p.ExitCode == 0 ? (true, output.Trim()) : (false, $"{exe} {p.ExitCode}: {output.Trim()}");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return (false, e.Message);
        }
    }

    static string Win32Message(string head) => $"{head}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
}
