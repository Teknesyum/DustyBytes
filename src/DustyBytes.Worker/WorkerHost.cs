using System.Globalization;
using DustyBytes.Core;

namespace DustyBytes.Worker;

public static class WorkerHost
{
    public const int ExitOk = 0;
    public const int ExitUsage = 64;
    public const int ExitNoParent = 65;
    public const int ExitFailure = 70;

    public static int Run(string[] args)
    {
        string? pipe = null;
        int? parent = null;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--worker":
                    break;
                case "--pipe" when i + 1 < args.Length:
                    pipe = args[++i];
                    break;
                case "--parent" when i + 1 < args.Length:
                    parent = int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
            }
        }

        if (pipe is null || parent is null || !WorkerWire.IsValidPipeName(pipe))
            return ExitUsage;
        if (dryRun)
            Environment.SetEnvironmentVariable(DryRun.Variable, "1");

        try
        {
            using var parentProcess = System.Diagnostics.Process.GetProcessById(parent.Value);
        }
        catch (ArgumentException)
        {
            return ExitNoParent;
        }

        try
        {
            var services = WorkerServices.CreateDefault();
            _ = Task.Run(() =>
            {
                try
                {
                    services.Quarantine.PurgeExpired();
                }
                catch (Exception)
                {
                }
            });
            var server = new WorkerServer(pipe, parent.Value, services);
            server.RunAsync().GetAwaiter().GetResult();
            return ExitOk;
        }
        catch (Exception)
        {
            return ExitFailure;
        }
    }
}
