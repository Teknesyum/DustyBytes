using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public sealed record VendorCommand(string CommandLine, bool Silent, string Description);

public sealed record VendorResult(bool Ok, bool Ran, int ExitCode, string Message, bool RebootRequired = false);

public sealed class Uninstaller
{
    readonly LeftoverScanner _scanner;

    public Uninstaller(LeftoverScanner scanner) => _scanner = scanner;

    public Func<string, string?, Action<int>?, CancellationToken, Task<ProcessRunResult>> Runner { get; init; } = ProcessTree.RunAndWaitTree;

    public RestorePointResult CreateRestorePoint(string description, IProgress<ScanProgress>? progress = null)
    {
        progress?.Report(new ScanProgress("Geri yükleme noktası", 0, description));
        var r = RestorePoint.Create(description);
        progress?.Report(new ScanProgress("Geri yükleme noktası", 100, r.Message));
        return r;
    }

    public LeftoverSnapshot Snapshot(InstalledProgram program, IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        _scanner.Snapshot(program, progress, ct);

    public LeftoverSnapshot Diff(LeftoverSnapshot snapshot, IProgress<ScanProgress>? progress = null)
    {
        progress?.Report(new ScanProgress("Kalıntı farkı", 0));
        var d = _scanner.Diff(snapshot);
        progress?.Report(new ScanProgress("Kalıntı farkı", 100, $"{d.Candidates.Count} kalıntı"));
        return d;
    }

    public static VendorCommand? BuildCommand(InstalledProgram p)
    {
        if (p.Source == ProgramSource.Msix)
            return null;
        if (p.WindowsInstaller && p.ProductCode is { } pc && MsiGuid.TryParseBraced(pc, out _))
            return new VendorCommand($"msiexec.exe /x {pc} /qb /norestart", true, "Windows Installer, temel arayüzle");
        if (!string.IsNullOrWhiteSpace(p.QuietUninstallString))
            return new VendorCommand(p.QuietUninstallString!, true, "Üreticinin sessiz kaldırıcısı");
        if (!string.IsNullOrWhiteSpace(p.UninstallString))
        {
            var cmd = p.UninstallString!;
            if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase) && p.ProductCode is { } code)
                return new VendorCommand($"msiexec.exe /x {code} /qb /norestart", true, "Windows Installer, temel arayüzle");
            return new VendorCommand(cmd, false, "Üreticinin kaldırıcısı, görünür");
        }
        return null;
    }

    public async Task<VendorResult> RunVendorUninstaller(InstalledProgram program, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        if (program.NoRemove)
            return new(false, false, -1, "Program kaldırılamaz olarak işaretli (NoRemove)");

        if (program.Source == ProgramSource.Msix)
        {
            if (program.PackageFullName is not { } full)
                return new(false, false, -1, "Paket tam adı yok");
            if (DryRun.Enabled)
                return new(true, false, 0, $"Prova kipi: paket kaldırılmadı ({full})");
            progress?.Report(new ScanProgress("Store paketi kaldırılıyor", 0, full));
            var pr = await MsixPackages.RemoveAsync(full, new Progress<double>(v => progress?.Report(new ScanProgress("Store paketi kaldırılıyor", v))), ct).ConfigureAwait(false);
            return new(pr.Ok, true, pr.Ok ? 0 : -1, pr.Message);
        }

        var command = BuildCommand(program);
        if (command is null)
            return new(false, false, -1, "Kaldırma komutu yok");
        if (DryRun.Enabled)
            return new(true, false, 0, $"Prova kipi: çalıştırılmadı: {command.CommandLine}");

        progress?.Report(new ScanProgress("Üreticinin kaldırıcısı", 0, command.Description + ": " + command.CommandLine));
        var exe = CommandLine.Executable(command.CommandLine);
        var workDir = exe is not null && Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;
        if (workDir is not null && !Directory.Exists(workDir))
            workDir = null;
        var run = await Runner(command.CommandLine, workDir, active => progress?.Report(new ScanProgress("Üreticinin kaldırıcısı", 50, $"{active} süreç çalışıyor")), ct).ConfigureAwait(false);
        if (!run.Started)
            return new(false, false, -1, run.Message);
        if (!run.Completed)
            return new(false, true, -1, run.Message);
        var (ok, reboot, message) = Interpret(program, run.ExitCode);
        progress?.Report(new ScanProgress("Üreticinin kaldırıcısı", 100, message));
        return new(ok, true, run.ExitCode, message, reboot);
    }

    public static (bool Ok, bool Reboot, string Message) Interpret(InstalledProgram p, int code)
    {
        var msi = p.WindowsInstaller || p.Installer == InstallerType.Msi;
        return code switch
        {
            0 => (true, false, "Kaldırıcı başarıyla bitti"),
            3010 or 1641 when msi => (true, true, "Kaldırıldı; yeniden başlatma gerekiyor"),
            1605 when msi => (true, false, "Ürün zaten kurulu değil"),
            1602 when msi => (false, false, "Kullanıcı kaldırmayı iptal etti"),
            1618 when msi => (false, false, "Başka bir kurulum sürüyor; sonra yeniden deneyin"),
            _ => (false, false, $"Kaldırıcı {code} koduyla bitti"),
        };
    }
}
