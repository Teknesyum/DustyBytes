using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public sealed record VendorCommand(string CommandLine, bool Silent, string Description);

public sealed record VendorResult(bool Ok, bool Ran, int ExitCode, string Message, bool RebootRequired = false);

public sealed class Uninstaller
{
    readonly LeftoverScanner _scanner;

    public Uninstaller(LeftoverScanner scanner) => _scanner = scanner;

    public const string SilentStep = "Sessiz kaldırıcı";
    public const string VisibleStep = "Görünür kaldırıcı";
    public const string VisibleCard = "Üreticinin kaldırıcısı açıldı, sihirbazı siz bitirin; biz bekliyoruz";

    public Func<RunRequest, CancellationToken, Task<ProcessRunResult>> Runner { get; init; } = ProcessTree.Run;

    public TimeSpan SilentTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public string? LogDir { get; init; } = Path.Combine(Paths.AppData, "uninstall-logs");

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

    public static VendorCommand? BuildCommand(InstalledProgram p) => SilentCommand(p) ?? VisibleCommand(p);

    static string? MsiCode(InstalledProgram p) =>
        p.ProductCode is { } pc && MsiGuid.TryParseBraced(pc, out _)
        && (p.WindowsInstaller || p.UninstallString?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true)
            ? pc
            : null;

    static string Append(string command, params string[] flags)
    {
        var result = command.TrimEnd();
        foreach (var f in flags)
            if (!result.Contains(f, StringComparison.OrdinalIgnoreCase))
                result += " " + f;
        return result;
    }

    public static VendorCommand? SilentCommand(InstalledProgram p, string? msiLog = null)
    {
        if (p.Source == ProgramSource.Msix)
            return null;
        if (!string.IsNullOrWhiteSpace(p.QuietUninstallString))
            return new VendorCommand(p.QuietUninstallString!, true, "Üreticinin sessiz kaldırıcısı");
        if (MsiCode(p) is { } code)
            return new VendorCommand($"msiexec.exe /x {code} /qn /norestart REBOOT=ReallySuppress" + (msiLog is null ? "" : $" /l*v \"{msiLog}\""), true, "Windows Installer, sessiz");
        if (string.IsNullOrWhiteSpace(p.UninstallString))
            return null;
        var cmd = p.UninstallString!;
        return p.Installer switch
        {
            InstallerType.Inno => new VendorCommand(Append(cmd, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"), true, "Inno Setup, sessiz"),
            InstallerType.Nsis => new VendorCommand(Append(cmd, "/S"), true, "NSIS, sessiz"),
            InstallerType.Burn => new VendorCommand(Append(cmd, "/uninstall", "/quiet", "/norestart"), true, "WiX paketi, sessiz"),
            InstallerType.Squirrel => new VendorCommand(Append(cmd, "--uninstall", "-s"), true, "Squirrel, sessiz"),
            _ => null,
        };
    }

    public static VendorCommand? VisibleCommand(InstalledProgram p)
    {
        if (p.Source == ProgramSource.Msix)
            return null;
        if (MsiCode(p) is { } code)
            return new VendorCommand($"msiexec.exe /x {code}", false, "Windows Installer, görünür");
        if (!string.IsNullOrWhiteSpace(p.UninstallString))
            return new VendorCommand(p.UninstallString!, false, "Üreticinin kaldırıcısı, görünür");
        return null;
    }

    Task<ProcessRunResult> Run(VendorCommand command, string step, TimeSpan? timeout, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var exe = CommandLine.Executable(command.CommandLine);
        var workDir = exe is not null && Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;
        if (workDir is not null && !Directory.Exists(workDir))
            workDir = null;
        return Runner(new RunRequest(command.CommandLine, workDir, active => progress?.Report(new ScanProgress(step, 50, $"{active} süreç çalışıyor")), timeout), ct);
    }

    string? MsiLogFile(InstalledProgram program)
    {
        if (MsiCode(program) is null || LogDir is not { } dir)
            return null;
        try
        {
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{MsiCode(program)!.Trim('{', '}')}.log");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

        var silent = SilentCommand(program, DryRun.Enabled ? null : MsiLogFile(program));
        var visible = VisibleCommand(program);
        if (silent is null && visible is null)
            return new(false, false, -1, "Kaldırma komutu yok");
        if (DryRun.Enabled)
            return new(true, false, 0, $"Prova kipi: çalıştırılmadı: {(silent ?? visible)!.CommandLine}");

        VendorResult? first = null;
        if (silent is not null)
        {
            progress?.Report(new ScanProgress(SilentStep, 0, silent.Description + ": " + silent.CommandLine));
            var run = await Run(silent, SilentStep, SilentTimeout, progress, ct).ConfigureAwait(false);
            first = Result(program, run);
            if (run.Completed && !_scanner.IsStillInstalled(program))
            {
                var done = first.Ok ? first : first with { Ok = true, Message = $"{first.Message}; program kaldırılmış görünüyor" };
                progress?.Report(new ScanProgress(SilentStep, 100, done.Message));
                return done;
            }
            if (ct.IsCancellationRequested || visible is null || run is { Completed: true, ExitCode: 1602 or 1618 })
                return first;
        }

        progress?.Report(new ScanProgress(VisibleStep, 0, VisibleCard));
        var shown = await Run(visible!, VisibleStep, null, progress, ct).ConfigureAwait(false);
        var result = Result(program, shown);
        if (first is not null)
            result = result with { Message = $"Sessiz kaldırma olmadı ({first.Message}); görünür kaldırıcı: {result.Message}" };
        progress?.Report(new ScanProgress(VisibleStep, 100, result.Message));
        return result;
    }

    static VendorResult Result(InstalledProgram program, ProcessRunResult run)
    {
        if (!run.Started)
            return new(false, false, -1, run.Message);
        if (!run.Completed)
            return new(false, true, -1, run.Message);
        var (ok, reboot, message) = Interpret(program, run.ExitCode);
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
