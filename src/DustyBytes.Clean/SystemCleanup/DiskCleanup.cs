using DustyBytes.Core;
using Microsoft.Win32;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class DiskCleanup(
    Func<string, long>? freeSpace = null,
    Func<string, string, IProgress<string>?, CancellationToken, Task<(int ExitCode, string Output)>>? runner = null,
    string? windowsDir = null) : ISystemCleanupTask
{
    public const int SageNumber = 1974;
    public const string VolumeCachesKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    public static readonly string[] Handlers = ["Previous Installations", "Temporary Setup Files"];

    public string Id => "disk-cleanup";
    public string Name => "Disk Temizleme (Önceki Kurulumlar)";

    public static string StateFlagsValueName => $"StateFlags{SageNumber:D4}";

    string WindowsDir => windowsDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    string SystemDrive => Path.GetPathRoot(WindowsDir) is { Length: > 0 } root ? root : WindowsDir[..3];

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var windowsOld = Path.Combine(Path.GetDirectoryName(WindowsDir.TrimEnd('\\', '/')) ?? SystemDrive, "Windows.old");
        var bytes = FolderSize.Measure(windowsOld);
        return Task.FromResult(new SystemCleanupEstimate(
            Id, bytes, bytes > 0,
            $"{windowsOld} (yaklaşık, gerçek değer cleanmgr /sagerun ile ölçülür)"));
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

        var drive = SystemDrive;
        var before = Free(drive);
        var (exitCode, _) = await (runner ?? ProcessRunner.RunAsync)("cleanmgr.exe", BuildSagerunArguments(), progress, ct);
        var after = Free(drive);
        var freed = before >= 0 && after >= 0 ? Math.Max(0, after - before) : 0;
        return exitCode == 0
            ? new SystemCleanupResult(Id, true, "Disk temizleme tamamlandı", freed)
            : new SystemCleanupResult(Id, false, $"cleanmgr çıktı kodu {exitCode}", freed);
    }

    long Free(string root)
    {
        try
        {
            return (freeSpace ?? MeasureFree)(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return -1;
        }
    }

    static long MeasureFree(string root) => new DriveInfo(root).AvailableFreeSpace;

    public static string BuildSagerunArguments() => $"/sagerun:{SageNumber}";

    public static string BuildSagesetArguments() => $"/sageset:{SageNumber}";

    public static void WriteStateFlag(string handler, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\{handler}");
        key.SetValue(StateFlagsValueName, value, RegistryValueKind.DWord);
    }
}
