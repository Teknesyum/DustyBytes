using System.Diagnostics;
using System.Security;
using System.Text;

namespace DustyBytes.App.Services;

public sealed record CommandResult(int ExitCode, string Output);

public interface ICommandRunner
{
    CommandResult Run(string file, IReadOnlyList<string> args);
}

public sealed class ProcessRunner : ICommandRunner
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    public CommandResult Run(string file, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(file + " başlatılamadı");
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Limit))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            return new CommandResult(-1, "");
        }
        return new CommandResult(process.ExitCode, output.Result);
    }
}

public sealed class WeeklyCheckTask(ICommandRunner runner, string exePath, string? tempDir = null)
{
    public const string TaskName = "DustyBytes disk kontrolü";
    public const string Tool = "schtasks.exe";

    public string ExePath { get; } = exePath;

    public string Definition() =>
        $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>DustyBytes</Author>
            <Description>Haftada bir boş alanı ölçer; azsa bildirim gösterir. Hiçbir dosyayı silmez.</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2026-01-05T12:00:00</StartBoundary>
              <Enabled>true</Enabled>
              <ScheduleByWeek>
                <DaysOfWeek><Monday /></DaysOfWeek>
                <WeeksInterval>1</WeeksInterval>
              </ScheduleByWeek>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(ExePath)}</Command>
              <Arguments>{LaunchArgs.Check}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    public bool IsCurrent()
    {
        var query = runner.Run(Tool, ["/Query", "/TN", TaskName, "/XML"]);
        return query.ExitCode == 0
            && query.Output.Contains(SecurityElement.Escape(ExePath)!, StringComparison.OrdinalIgnoreCase)
            && query.Output.Contains(LaunchArgs.Check, StringComparison.Ordinal);
    }

    public bool Exists() => runner.Run(Tool, ["/Query", "/TN", TaskName]).ExitCode == 0;

    public bool Ensure(bool enabled) => enabled ? Register() : Remove();

    bool Register()
    {
        if (IsCurrent())
            return true;
        var file = Path.Combine(tempDir ?? Path.GetTempPath(), $"dustybytes-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, Definition(), Encoding.Unicode);
            return runner.Run(Tool, ["/Create", "/TN", TaskName, "/XML", file, "/F"]).ExitCode == 0;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    bool Remove()
    {
        if (!Exists())
            return true;
        return runner.Run(Tool, ["/Delete", "/TN", TaskName, "/F"]).ExitCode == 0;
    }
}
