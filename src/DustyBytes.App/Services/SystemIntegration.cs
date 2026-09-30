using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DustyBytes.App.Services;

public sealed record IntegrationResult(bool ShellOk, bool TaskOk, string? Error);

public sealed class SystemIntegration(ShellIntegration shell, WeeklyCheckTask task)
{
    public ShellIntegration Shell { get; } = shell;
    public WeeklyCheckTask Weekly { get; } = task;

    public static string CurrentExe =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DustyBytes.exe");

    public static SystemIntegration ForCurrentUser()
    {
        var exe = CurrentExe;
        return new SystemIntegration(new ShellIntegration(new CurrentUserRegistry(), exe), new WeeklyCheckTask(new ProcessRunner(), exe));
    }

    static bool Failure(Exception e) =>
        e is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or InvalidOperationException;

    public IntegrationResult Apply(bool weekly)
    {
        string? error = null;
        var shellOk = Try(() => Shell.Ensure(), ref error);
        var taskOk = false;
        try
        {
            taskOk = Weekly.Ensure(weekly);
            if (!taskOk)
                error ??= weekly ? "Haftalık görev kurulamadı" : "Haftalık görev kaldırılamadı";
        }
        catch (Exception e) when (Failure(e))
        {
            error ??= e.Message;
        }
        return new IntegrationResult(shellOk, taskOk, error);
    }

    public IntegrationResult SetWeekly(bool weekly)
    {
        try
        {
            var ok = Weekly.Ensure(weekly);
            return new IntegrationResult(true, ok, ok ? null : weekly ? "Haftalık görev kurulamadı" : "Haftalık görev kaldırılamadı");
        }
        catch (Exception e) when (Failure(e))
        {
            return new IntegrationResult(true, false, e.Message);
        }
    }

    public IntegrationResult Unregister()
    {
        string? error = null;
        var taskOk = false;
        try
        {
            taskOk = Weekly.Ensure(false);
        }
        catch (Exception e) when (Failure(e))
        {
            error = e.Message;
        }
        var shellOk = Try(() => Shell.Remove(), ref error);
        return new IntegrationResult(shellOk, taskOk, error);
    }

    static bool Try(Func<int> action, ref string? error)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (Failure(e))
        {
            error ??= e.Message;
            return false;
        }
    }
}

public sealed class WindowsToast(ShellIntegration shell) : INotifier
{
    public bool Show(string title, string body, string launch)
    {
        try
        {
            shell.Ensure();
            var xml = new XmlDocument();
            xml.LoadXml(
                $"<toast activationType=\"protocol\" launch=\"{SecurityElement.Escape(launch)}\">" +
                "<visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
                "</binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(ShellIntegration.AppId).Show(new ToastNotification(xml));
            return true;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }
}
