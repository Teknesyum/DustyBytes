using System.Text.RegularExpressions;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.Uninstall;

public static partial class ForceUninstall
{
    public const string FilesStep = "force-files";
    public const string EntryStep = "force-entry";

    static readonly string[] UpdateReleaseTypes = ["Update", "Hotfix", "Security Update", "Service Pack", "Update Rollup"];
    static readonly string[] DriverTools = ["dpinst", "difx", "pnputil", "drvinst", "drvload"];

    [GeneratedRegex(@"\b(driver|drivers|sürücü|sürücüsü|sürücüleri)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DriverName();

    [GeneratedRegex(@"^(security update|update for|hotfix for|service pack|güvenlik güncelleştirmesi)\b|\bKB\d{6,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdateName();

    public static string? Refusal(InstalledProgram p)
    {
        if (p.Source == ProgramSource.Msix)
            return "Store paketleri zorla kaldırılmaz; Windows'un paket yöneticisi kaldırır";
        if (p.NoRemove)
            return "Program kaldırılamaz olarak işaretli";
        if (p.IsFramework)
            return "Çerçeve paketleri zorla kaldırılmaz";
        if (UpdateName().IsMatch(p.DisplayName))
            return "Windows güncellemeleri zorla kaldırılmaz";
        if (DriverName().IsMatch(p.DisplayName) || Commands(p).Any(c => DriverTools.Any(t => c.Contains(t, StringComparison.OrdinalIgnoreCase))))
            return "Sürücüler zorla kaldırılmaz";
        if (IsWindowsComponent(p))
            return "Windows bileşenleri zorla kaldırılmaz";
        return null;
    }

    public static string? Refusal(InstalledProgram p, IRegistryView reg, ProtectedList protection)
    {
        if (Refusal(p) is { } basic)
            return basic;
        if (protection.RuntimeReason(p.DisplayName) is { } runtime)
            return $"Korumalı: {runtime}";
        if (p.Key is not { } key || !reg.KeyExists(key))
            return "Programın kayıt girdisi bulunamadı";
        if (reg.GetNumber(key, "SystemComponent") == 1)
            return "Sistem bileşeni olarak işaretli; zorla kaldırılmaz";
        if (reg.GetString(key, "ParentKeyName") is not null)
            return "Başka bir ürünün güncellemesi ya da parçası; zorla kaldırılmaz";
        if (reg.GetString(key, "ReleaseType") is { } rt && UpdateReleaseTypes.Contains(rt, StringComparer.OrdinalIgnoreCase))
            return "Windows güncellemeleri zorla kaldırılmaz";
        return null;
    }

    public static string? BrokenReason(InstalledProgram p, IRegistryView reg, IFileProbe probe)
    {
        if (p.Source == ProgramSource.Msix || p.NoRemove)
            return null;
        var commands = Commands(p).ToList();
        var msi = p.WindowsInstaller && p.ProductCode is not null;
        if (commands.Count == 0)
            return msi ? MsiReason(p, reg) : "Kaldırma komutu kayıtlı değil";
        string? first = null;
        foreach (var command in commands)
        {
            var reason = CommandReason(p, command, reg, probe);
            if (reason is null)
                return null;
            first ??= reason;
        }
        return first;
    }

    static string? CommandReason(InstalledProgram p, string command, IRegistryView reg, IFileProbe probe)
    {
        var exe = CommandLine.Executable(command);
        if (exe is null)
            return "Kaldırma komutu okunamıyor";
        if (exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            return MsiReason(p, reg);
        if (!Path.IsPathRooted(exe))
            return null;
        return probe.FileExists(exe) ? null : $"Kaldırıcı dosyası yok: {exe}";
    }

    static string? MsiReason(InstalledProgram p, IRegistryView reg) =>
        p.ProductCode is not { } code || p.PerUser || InstalledPrograms.MsiProductRegistered(reg, code)
            ? null
            : "Windows Installer bu ürünü artık tanımıyor";

    static IEnumerable<string> Commands(InstalledProgram p) =>
        new[] { p.QuietUninstallString, p.UninstallString }.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!);

    static bool IsWindowsComponent(InstalledProgram p) =>
        p.Publisher is { } pub && pub.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
        && (pub.Equals("Microsoft Windows", StringComparison.OrdinalIgnoreCase)
            || p.DisplayName.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase)
            || p.DisplayName.StartsWith("Microsoft Windows", StringComparison.OrdinalIgnoreCase));

    public static bool IsEntry(LeftoverCandidate c, InstalledProgram p) =>
        c.Kind == LeftoverKind.RegistryKey && c.Key is { } k && p.Key is { } pk && k.Identity == pk.Identity;

    public static bool IsInstallFolder(LeftoverCandidate c) =>
        c.Kind == LeftoverKind.Folder && c.Evidence.Any(e => e.Code is "install-dir" or "install-dir-inferred");
}
