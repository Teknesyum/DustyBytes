namespace DustyBytes.Clean.SystemCleanup;

public sealed record SystemCleanupEstimate(
    string TaskId,
    long RecoverableBytes,
    bool Recommended,
    string Detail,
    string? Warning = null,
    bool Silent = true,
    bool Available = true,
    string? Note = null,
    string? RestoreId = null);

public sealed record SystemCleanupResult(string TaskId, bool Ok, string Message, long FreedBytes);

public interface ISystemCleanupTask
{
    string Id { get; }
    string Name { get; }
    bool ExplicitOnly => false;
    IReadOnlyList<string> ExtraIds => [];
    Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct);
    Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct);
    Task<SystemCleanupResult> RunAsync(string id, IProgress<string> progress, CancellationToken ct) => RunAsync(progress, ct);
}
