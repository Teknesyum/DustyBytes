using DustyBytes.Core;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class DeliveryOptimizationCleanup : ISystemCleanupTask
{
    public const string PowerShellCommand = "Delete-DeliveryOptimizationCache -Force";

    public string Id => "delivery-optimization";
    public string Name => "Delivery Optimization Önbelleği";

    public static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var bytes = FolderSize.Measure(CacheDir);
        return Task.FromResult(new SystemCleanupEstimate(Id, bytes, bytes > 0, CacheDir));
    }

    public async Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (DryRun.Enabled)
        {
            progress.Report($"[prova] powershell -Command \"{PowerShellCommand}\"");
            return new SystemCleanupResult(Id, true, "Prova kipi: servis çağrılmadı", 0);
        }

        var before = FolderSize.Measure(CacheDir);
        var (exitCode, _) = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{PowerShellCommand}\"",
            progress,
            ct);
        var after = FolderSize.Measure(CacheDir);
        var freed = Math.Max(0, before - after);

        return exitCode == 0
            ? new SystemCleanupResult(Id, true, "Delivery Optimization önbelleği temizlendi", freed)
            : new SystemCleanupResult(Id, false, $"PowerShell çıktı kodu {exitCode}", 0);
    }
}
