using DustyBytes.Core;
using Microsoft.Win32;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class DiskCleanup : ISystemCleanupTask
{
    public const int SageNumber = 1974;
    public const string VolumeCachesKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    public static readonly string[] Handlers = ["Previous Installations", "Temporary Setup Files"];

    public string Id => "disk-cleanup";
    public string Name => "Disk Temizleme (Önceki Kurulumlar)";

    public static string StateFlagsValueName => $"StateFlags{SageNumber:D4}";

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..3];
        var windowsOld = Path.Combine(systemDrive, "Windows.old");
        var setupTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Panther");

        var bytes = FolderSize.Measure(windowsOld) + FolderSize.Measure(setupTemp);
        return Task.FromResult(new SystemCleanupEstimate(
            Id, bytes, bytes > 0,
            $"{windowsOld} + {setupTemp} (yaklaşık, gerçek değer cleanmgr /sagerun ile ölçülür)"));
    }

    public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct) =>
        RunAsync(progress, ct, registryWriter: WriteStateFlag);

    public async Task<SystemCleanupResult> RunAsync(
        IProgress<string> progress,
        CancellationToken ct,
        Action<string, int> registryWriter)
    {
        if (DryRun.Enabled)
        {
            foreach (var handler in Handlers)
                progress.Report($"[prova] {VolumeCachesKey}\\{handler}\\{StateFlagsValueName} = 2");
            progress.Report($"[prova] cleanmgr.exe {BuildSagerunArguments()}");
            return new SystemCleanupResult(Id, true, "Prova kipi: cleanmgr çalıştırılmadı", 0);
        }

        foreach (var handler in Handlers)
        {
            progress.Report($"{handler} işaretleniyor");
            registryWriter(handler, 2);
        }

        var (exitCode, _) = await ProcessRunner.RunAsync("cleanmgr.exe", BuildSagerunArguments(), progress, ct);
        return exitCode == 0
            ? new SystemCleanupResult(Id, true, "Disk temizleme tamamlandı", 0)
            : new SystemCleanupResult(Id, false, $"cleanmgr çıktı kodu {exitCode}", 0);
    }

    public static string BuildSagerunArguments() => $"/sagerun:{SageNumber}";

    public static string BuildSagesetArguments() => $"/sageset:{SageNumber}";

    public static void WriteStateFlag(string handler, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\{handler}");
        key.SetValue(StateFlagsValueName, value, RegistryValueKind.DWord);
    }
}
