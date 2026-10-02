using System.Text.Json.Serialization;

namespace DustyBytes.Core.Ipc;

public static class Ops
{
    public const string Ping = "ping";
    public const string Quarantine = "quarantine";
    public const string Delete = "delete";
    public const string Restore = "restore";
    public const string Purge = "purge";
    public const string ListQuarantine = "list-quarantine";
    public const string Uninstall = "uninstall";
    public const string RemoveLeftovers = "remove-leftovers";
    public const string ForceUninstall = "force-uninstall";
    public const string Clean = "clean";
    public const string CleanPreview = "clean-preview";
    public const string SystemClean = "system-clean";
    public const string FastScan = "fast-scan";
    public const string UsnRefresh = "usn-refresh";
    public const string LockInfo = "lock-info";
    public const string Compress = "compress";
    public const string Uncompress = "uncompress";
    public const string CloudFree = "cloud-free";
    public const string CloudKeep = "cloud-keep";
    public const string Shutdown = "shutdown";
}

public static class Targets
{
    public const string Expired = "expired";
    public const string All = "all";
}

public sealed record WorkerRequest
{
    public required string Op { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public List<string> Paths { get; init; } = [];
    public bool UserApproved { get; init; }
    public bool IncludeUserData { get; init; }
    public string? UnitId { get; init; }
    public string? Target { get; init; }
    public List<string> Items { get; init; } = [];
    public string? Digest { get; init; }
    public string? SessionId { get; init; }
    public string? PreviewId { get; init; }
    public List<string> Roots { get; init; } = [];
}

public sealed record ItemResult(string Path, bool Ok, string Message, long Bytes = 0);

public sealed record WorkerResponse
{
    public required string Id { get; init; }
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public bool DryRun { get; init; }
    public List<ItemResult> Items { get; init; } = [];
    public long PendingBytes { get; init; }
    public long FreedBytes { get; init; }
    public string? Payload { get; init; }
    public PathTally? Tally { get; init; }
    public bool Stale { get; init; }
}

public sealed record WorkerProgress(string Id, string Step, double Percent, string? Line);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerResponse))]
[JsonSerializable(typeof(WorkerProgress))]
[JsonSerializable(typeof(CleanPreview))]
public partial class IpcJson : JsonSerializerContext;
