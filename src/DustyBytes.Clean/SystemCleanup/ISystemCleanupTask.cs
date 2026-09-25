namespace DustyBytes.Clean.SystemCleanup;

public sealed record SystemCleanupEstimate(string TaskId, long RecoverableBytes, bool Recommended, string Detail);

public sealed record SystemCleanupResult(string TaskId, bool Ok, string Message, long FreedBytes);

public interface ISystemCleanupTask
{
    string Id { get; }
    string Name { get; }
    Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct);
    Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct);
}
