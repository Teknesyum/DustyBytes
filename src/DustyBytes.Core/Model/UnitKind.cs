namespace DustyBytes.Core.Model;

public enum UnitKind
{
    Game,
    Program,
    AppContent,
    Film,
    Series,
    DevArtifact,
    Cache,
    BrowserCache,
    Installer,
    SystemArtifact,
    Folder,
    CloudCopy,
}

public enum RemovalMethod
{
    Quarantine,
    DirectDelete,
    Launcher,
    Uninstaller,
    SystemTool,
    CloudOnly,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}
