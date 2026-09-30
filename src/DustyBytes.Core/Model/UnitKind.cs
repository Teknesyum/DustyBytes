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
    OldDownload,
    Duplicate,
}

public enum RemovalMethod
{
    Quarantine,
    DirectDelete,
    Launcher,
    Uninstaller,
    SystemTool,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}
