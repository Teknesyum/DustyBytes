namespace DustyBytes.Clean.Uninstall;

public enum UninstallMode
{
    None,
    Silent,
    Visible,
}

public sealed record UninstallPlan(UninstallMode Mode, InstallerType Installer, string Description)
{
    public const string SilentBadge = "Sessiz kaldırılır";
    public const string VisibleBadge = "Kaldırıcı penceresi açılır";

    public bool IsSilent => Mode == UninstallMode.Silent;
    public bool OpensWindow => Mode == UninstallMode.Visible;

    public string Badge => Mode switch
    {
        UninstallMode.Silent => SilentBadge,
        UninstallMode.Visible => VisibleBadge,
        _ => "",
    };
}
