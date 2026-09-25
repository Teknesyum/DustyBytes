namespace DustyBytes.Clean.Uninstall;

public enum InstallerType
{
    Unknown,
    Msi,
    Inno,
    Nsis,
    InstallShield,
    Msix,
}

public enum ProgramSource
{
    Registry,
    Msix,
}

public sealed record InstalledProgram
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ProgramSource Source { get; init; }
    public string? Publisher { get; init; }
    public string? DisplayVersion { get; init; }
    public string? InstallLocation { get; init; }
    public DateOnly? InstallDate { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public long SizeBytes { get; init; }
    public bool SizeMeasured { get; init; }
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public bool WindowsInstaller { get; init; }
    public string? ProductCode { get; init; }
    public string? DisplayIcon { get; init; }
    public InstallerType Installer { get; init; }
    public RegKeyRef? Key { get; init; }
    public string? KeyName { get; init; }
    public bool Is64Bit { get; init; }
    public bool PerUser { get; init; }
    public string? PackageFullName { get; init; }
    public string? PackageFamilyName { get; init; }
    public bool IsFramework { get; init; }
    public bool NoRemove { get; init; }

    public bool CanUninstall =>
        !NoRemove && (Source == ProgramSource.Msix
            ? !string.IsNullOrEmpty(PackageFullName)
            : ProductCode is not null && WindowsInstaller || !string.IsNullOrWhiteSpace(QuietUninstallString) || !string.IsNullOrWhiteSpace(UninstallString));
}
