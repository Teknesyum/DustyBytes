using System.Collections.Concurrent;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Worker;

public static class WorkerHandlers
{
    static readonly ConcurrentDictionary<string, Func<WorkerRequest, IProgress<WorkerProgress>, CancellationToken, Task<WorkerResponse>>> Map =
        new(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.Ordinal)
    {
        Ops.Ping, Ops.Quarantine, Ops.Delete, Ops.Restore, Ops.Purge, Ops.ListQuarantine, Ops.LockInfo, Ops.Shutdown,
    };

    public static readonly IReadOnlySet<string> Destructive = new HashSet<string>(StringComparer.Ordinal)
    {
        Ops.Quarantine, Ops.Delete, Ops.Purge, Ops.Uninstall, Ops.RemoveLeftovers, Ops.ForceUninstall, Ops.Clean, Ops.SystemClean,
        Ops.Compress, Ops.Uncompress, Ops.CloudFree, Ops.CloudKeep,
    };

    public static void Register(string op, Func<WorkerRequest, IProgress<WorkerProgress>, CancellationToken, Task<WorkerResponse>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(op);
        ArgumentNullException.ThrowIfNull(handler);
        if (BuiltIn.Contains(op))
            throw new InvalidOperationException($"'{op}' worker'ın kendi işlemi; yeniden kaydedilemez");
        Map[op] = handler;
    }

    public static bool Unregister(string op) => Map.TryRemove(op, out _);

    public static bool TryGet(string op, out Func<WorkerRequest, IProgress<WorkerProgress>, CancellationToken, Task<WorkerResponse>> handler) =>
        Map.TryGetValue(op, out handler!);

    public static IReadOnlyCollection<string> Registered => [.. Map.Keys];
}
