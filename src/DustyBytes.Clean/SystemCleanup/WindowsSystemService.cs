using System.ServiceProcess;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class WindowsSystemService : ISystemService
{
    public bool IsRunning(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        return sc.Status == ServiceControllerStatus.Running;
    }

    public void Stop(string serviceName, TimeSpan timeout)
    {
        using var sc = new ServiceController(serviceName);
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }

    public void Start(string serviceName, TimeSpan timeout)
    {
        using var sc = new ServiceController(serviceName);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }
}
