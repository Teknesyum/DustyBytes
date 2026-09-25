namespace DustyBytes.Clean.SystemCleanup;

public interface ISystemService
{
    bool IsRunning(string serviceName);
    void Stop(string serviceName, TimeSpan timeout);
    void Start(string serviceName, TimeSpan timeout);
}
