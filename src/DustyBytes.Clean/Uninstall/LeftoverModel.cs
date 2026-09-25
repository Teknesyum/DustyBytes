namespace DustyBytes.Clean.Uninstall;

public enum LeftoverKind
{
    Folder,
    File,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    StartupEntry,
    Shortcut,
    FileAssociation,
    FirewallRule,
}

public enum ConfidenceTier
{
    Low,
    Medium,
    High,
}

public enum AnchorClass
{
    None,
    InstallFolder,
    VersionResource,
    Signer,
    Publisher,
    Name,
    UninstallKey,
    InstallTime,
}

public sealed record Evidence(string Code, int Points, AnchorClass Anchor, string Text);

public sealed record LeftoverCandidate
{
    public required string Id { get; init; }
    public required LeftoverKind Kind { get; init; }
    public required string Target { get; init; }
    public string? Detail { get; init; }
    public RegKeyRef? Key { get; init; }
    public string? ValueName { get; init; }
    public List<Evidence> Evidence { get; init; } = [];
    public int Score { get; init; }
    public int Anchors { get; init; }
    public ConfidenceTier Tier { get; init; }
    public string Reason { get; init; } = "";
    public long Bytes { get; init; }
    public bool Checked => Tier == ConfidenceTier.High;
}

public sealed record BlockedCandidate(LeftoverKind Kind, string Target, string Reason);

public sealed record LeftoverSnapshot
{
    public required string Id { get; init; }
    public required InstalledProgram Program { get; init; }
    public DateTime TakenAtUtc { get; init; } = DateTime.UtcNow;
    public List<LeftoverCandidate> Candidates { get; init; } = [];
    public List<BlockedCandidate> Blocked { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public bool IsDiff { get; init; }
    public bool ProgramStillInstalled { get; init; }
}
