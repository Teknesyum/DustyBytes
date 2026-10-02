using System.Text;
using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Uninstall;

public static partial class InstallerDetector
{
    const int HeadBytes = 512 * 1024;

    [GeneratedRegex(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InnoName();

    [GeneratedRegex(@"^(uninstall|uninst)([-_ ].*)?\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex NsisName();

    static readonly byte[] Nullsoft = Encoding.ASCII.GetBytes("Nullsoft");
    static readonly byte[] NsisError = Encoding.ASCII.GetBytes("NSIS Error");
    static readonly byte[] InstallShieldAscii = Encoding.ASCII.GetBytes("InstallShield");
    static readonly byte[] InnoAscii = Encoding.ASCII.GetBytes("Inno Setup");

    public static InstallerType Detect(string? keyName, bool windowsInstaller, string? uninstallString, string? quietUninstallString, IRegistryView? reg, RegKeyRef? key, IFileProbe? probe)
    {
        var command = uninstallString ?? quietUninstallString;
        if (windowsInstaller || command?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true)
            return InstallerType.Msi;

        if (reg is not null && key is not null && (reg.ValueExists(key, "BundleCachePath") || reg.ValueExists(key, "BundleUpgradeCode")))
            return InstallerType.Burn;
        if (command is not null && command.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase) && command.Contains("/uninstall", StringComparison.OrdinalIgnoreCase))
            return InstallerType.Burn;
        if (command is not null && command.Contains("--uninstall", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(CommandLine.Executable(command) ?? "").Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
            return InstallerType.Squirrel;

        if (keyName?.EndsWith("_is1", StringComparison.OrdinalIgnoreCase) == true)
            return InstallerType.Inno;
        if (reg is not null && key is not null && reg.ValueExists(key, "Inno Setup: App Path"))
            return InstallerType.Inno;
        if (reg is not null && key is not null && reg.GetValueNames(key).Any(n => n.StartsWith("NSIS:", StringComparison.OrdinalIgnoreCase)))
            return InstallerType.Nsis;

        if (command is not null && (command.Contains("InstallShield Installation Information", StringComparison.OrdinalIgnoreCase)
            || command.Contains("-runfromtemp", StringComparison.OrdinalIgnoreCase)
            || command.Contains("isuninst", StringComparison.OrdinalIgnoreCase)))
            return InstallerType.InstallShield;

        var exe = CommandLine.Executable(command);
        var name = exe is null ? "" : Path.GetFileName(exe);
        if (InnoName().IsMatch(name))
            return InstallerType.Inno;

        if (exe is not null && probe is not null && probe.FileExists(exe) && probe.ReadHead(exe, HeadBytes) is { } head)
        {
            if (IndexOf(head, Nullsoft) >= 0 || IndexOf(head, NsisError) >= 0)
                return InstallerType.Nsis;
            if (IndexOf(head, InnoAscii) >= 0)
                return InstallerType.Inno;
            if (IndexOf(head, InstallShieldAscii) >= 0)
                return InstallerType.InstallShield;
            if (probe.VersionText(exe) is { } version)
            {
                if (version.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
                    return InstallerType.Inno;
                if (version.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) || version.Contains("NSIS", StringComparison.Ordinal))
                    return InstallerType.Nsis;
                if (version.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                    return InstallerType.InstallShield;
            }
        }

        if (NsisName().IsMatch(name))
            return InstallerType.Nsis;

        return InstallerType.Unknown;
    }

    static int IndexOf(byte[] hay, byte[] needle) => hay.AsSpan().IndexOf(needle);
}
