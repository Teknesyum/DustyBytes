using System.Runtime.InteropServices;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;
using Microsoft.Win32;

namespace DustyBytes.Clean.SystemCleanup;

public interface IPowerInfo
{
    bool? HasBattery();
    bool? HibernateEnabled();
    long? HiberfilBytes();
    bool? HibernateReduced() => null;
}

public sealed partial class WindowsPowerInfo : IPowerInfo
{
    public static readonly WindowsPowerInfo Instance = new();

    public bool? HasBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return null;
            return status.BatteryFlag switch
            {
                128 => false,
                255 => null,
                _ => true,
            };
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public bool? HibernateEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("HibernateEnabled") is int value ? value != 0 : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool? HibernateReduced()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("HiberFileType") is int value ? value == 1 : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public long? HiberfilBytes()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root))
            return null;
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
            return new DirectoryInfo(root).EnumerateFiles("hiberfil.sys", options).FirstOrDefault()?.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

public delegate Task<(int ExitCode, string Output)> CommandRunner(string fileName, string arguments, IProgress<string>? progress, CancellationToken ct);

public sealed class HibernationTask : ISystemCleanupTask
{
    public const string OffId = "hibernation";
    public const string OnId = "hibernation-on";
    public const string ReducedId = "hibernation-reduced";
    public const string FullId = "hibernation-full";
    public const string ReduceLabel = "Küçült (Hızlı Başlangıç korunur)";
    public const string FullLabel = "Tam boyuta döndür";

    public const string ReducedDetail =
        "Hazırda bekletme küçültülmüş durumda: dosya daha küçük, Hızlı Başlangıç çalışıyor, ama pil biterken tam hazırda bekletme yapılmaz. Tam boyuta döndür ile eski haline gelir.";

    public const string Explanation =
        "Hazırda bekletme dosyası (hiberfil.sys) kapatınca silinir. Windows'un Hızlı Başlangıç özelliği de bu dosyayı kullandığı için onunla birlikte kapanır; bilgisayar açılırken birkaç saniye daha bekleyebilirsiniz. Geri aç ile ikisi de istediğiniz an geri gelir.";

    public const string LaptopWarning =
        "Bu bir dizüstü bilgisayar. Hazırda bekletme, pil biterken açık işlerinizi korur; kullanıyorsanız kapatmayın.";

    readonly SafetyGate _gate;
    readonly IPowerInfo _power;
    readonly CommandRunner _run;

    public HibernationTask(SafetyGate gate, IPowerInfo? power = null, CommandRunner? runner = null)
    {
        _gate = gate;
        _power = power ?? WindowsPowerInfo.Instance;
        _run = runner ?? ProcessRunner.RunAsync;
    }

    public string Id => OffId;
    public string Name => "Hazırda bekletme dosyası";
    public bool ExplicitOnly => true;
    public IReadOnlyList<string> ExtraIds => [OnId, ReducedId, FullId];

    public static string PowerCfg => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

    bool Enabled(long? bytes) => _power.HibernateEnabled() ?? bytes is > 0;

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var bytes = _power.HiberfilBytes();
        if (!Enabled(bytes))
            return Task.FromResult(new SystemCleanupEstimate(
                Id, 0, false,
                "Hazırda bekletme kapalı; hiberfil.sys diskte yer tutmuyor.",
                Silent: false,
                Available: false,
                Note: "Kapalı. Geri aç ile hazırda bekletmeyi ve Hızlı Başlangıç'ı yeniden açabilirsiniz.",
                RestoreId: OnId));

        var desktop = _power.HasBattery() == false;
        var size = bytes ?? 0;
        var reduced = _power.HibernateReduced() == true;
        return Task.FromResult(new SystemCleanupEstimate(
            Id, size, desktop && size > 0,
            reduced ? ReducedDetail : Explanation,
            Warning: desktop ? null : LaptopWarning,
            Silent: false,
            RestoreId: reduced ? FullId : null,
            AltId: reduced ? null : ReducedId,
            AltLabel: reduced ? null : ReduceLabel,
            RestoreLabel: reduced ? FullLabel : null));
    }

    public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct) => Switch(false, progress, ct);

    public Task<SystemCleanupResult> RunAsync(string id, IProgress<string> progress, CancellationToken ct) =>
        id switch
        {
            OnId => Switch(true, progress, ct),
            ReducedId => Resize(true, progress, ct),
            FullId => Resize(false, progress, ct),
            _ => Switch(false, progress, ct),
        };

    async Task<SystemCleanupResult> Resize(bool reduced, IProgress<string> progress, CancellationToken ct)
    {
        var id = reduced ? ReducedId : FullId;
        if (!_gate.SystemOpAllowed(SafetyGate.HibernateOp))
            return new SystemCleanupResult(id, false, "Korumalı liste hazırda bekletme ayarına izin vermiyor", 0);

        var args = reduced ? "/h /type reduced" : "/h /type full";
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Delete, "hiberfil.sys", $"powercfg {args}");
            progress.Report($"[prova] powercfg {args}");
            return new SystemCleanupResult(id, true, "Prova kipi: powercfg çağrılmadı", 0);
        }

        var before = _power.HiberfilBytes() ?? 0;
        var (exitCode, _) = await _run(PowerCfg, args, progress, ct);
        if (exitCode != 0)
            return new SystemCleanupResult(id, false, $"powercfg çıktı kodu {exitCode}", 0);
        if (!reduced)
            return new SystemCleanupResult(id, true, "Hazırda bekletme tam boyuta döndürüldü", 0);
        var after = _power.HiberfilBytes() ?? 0;
        return new SystemCleanupResult(id, true, "Hazırda bekletme küçültüldü; Hızlı Başlangıç korundu", Math.Max(0, before - after));
    }

    async Task<SystemCleanupResult> Switch(bool on, IProgress<string> progress, CancellationToken ct)
    {
        var id = on ? OnId : OffId;
        if (!_gate.SystemOpAllowed(SafetyGate.HibernateOp))
            return new SystemCleanupResult(id, false, "Korumalı liste hazırda bekletme ayarına izin vermiyor", 0);

        var args = on ? "/hibernate on" : "/hibernate off";
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Delete, "hiberfil.sys", $"powercfg {args}");
            progress.Report($"[prova] powercfg {args}");
            return new SystemCleanupResult(id, true, "Prova kipi: powercfg çağrılmadı", 0);
        }

        var before = _power.HiberfilBytes() ?? 0;
        var (exitCode, _) = await _run(PowerCfg, args, progress, ct);
        if (exitCode != 0)
            return new SystemCleanupResult(id, false, $"powercfg çıktı kodu {exitCode}", 0);
        if (on)
            return new SystemCleanupResult(id, true, "Hazırda bekletme ve Hızlı Başlangıç yeniden açıldı", 0);
        var after = _power.HiberfilBytes() ?? 0;
        return new SystemCleanupResult(id, true, "Hazırda bekletme kapatıldı; Geri aç ile yeniden açılabilir", Math.Max(0, before - after));
    }
}
