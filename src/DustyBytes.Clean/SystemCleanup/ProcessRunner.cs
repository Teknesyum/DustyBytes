using System.Diagnostics;

namespace DustyBytes.Clean.SystemCleanup;

public static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string arguments,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var output = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            output.AppendLine(e.Data);
            progress?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            output.AppendLine(e.Data);
            progress?.Report(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return (process.ExitCode, output.ToString());
    }
}
