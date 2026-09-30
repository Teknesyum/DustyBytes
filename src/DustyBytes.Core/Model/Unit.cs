namespace DustyBytes.Core.Model;

public sealed record Unit
{
    public required string Id { get; init; }
    public required UnitKind Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public required long SizeBytes { get; init; }
    public UsageSignal Usage { get; init; } = UsageSignal.Unknown;
    public double Confidence { get; init; } = 1.0;
    public RemovalMethod Removal { get; init; } = RemovalMethod.Quarantine;
    public bool ContainsUserData { get; init; }
    public string? LauncherUri { get; init; }
    public string Reason { get; init; } = "";
    public string Effect { get; init; } = "";
    public string? Label { get; init; }
    public double Score { get; init; }

    public string Drive => Paths.Count > 0 && System.IO.Path.GetPathRoot(Paths[0]) is { Length: > 0 } root ? root.ToUpperInvariant() : "";
}
