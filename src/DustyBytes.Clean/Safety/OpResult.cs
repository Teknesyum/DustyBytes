using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.Safety;

public enum OpStatus
{
    Done,
    DryRun,
    Denied,
    Conflict,
    Locked,
    NotFound,
    Failed,
    Scheduled,
}

public static class OpMethod
{
    public const string Quarantine = "quarantine";
    public const string RecycleBin = "recycle-bin";
    public const string Delete = "delete";
    public const string Restore = "restore";
    public const string Purge = "purge";
    public const string Reboot = "reboot";
}

public sealed record LockHolder(int Pid, string Name, string Kind, bool Restartable);

public sealed record OpResult
{
    public required string Path { get; init; }
    public required OpStatus Status { get; init; }
    public string Message { get; init; } = "";
    public string Method { get; init; } = "";
    public long Bytes { get; init; }
    public string? Id { get; init; }
    public Badge Badge { get; init; }
    public List<LockHolder> Holders { get; init; } = [];
    public List<string> Skipped { get; init; } = [];

    public bool Ok => Status is OpStatus.Done or OpStatus.DryRun or OpStatus.Scheduled;
    public bool IsDryRun => Status == OpStatus.DryRun;

    public static OpResult Denied(string path, Verdict verdict, string method) =>
        new() { Path = path, Status = OpStatus.Denied, Message = verdict.Reason, Badge = verdict.Badge, Method = method };

    public static OpResult NotFound(string path, string method) =>
        new() { Path = path, Status = OpStatus.NotFound, Message = "Yol bulunamadı", Method = method };

    public static OpResult Failed(string path, string method, string message) =>
        new() { Path = path, Status = OpStatus.Failed, Message = message, Method = method };

    public ItemResult ToItem()
    {
        var text = Message;
        if (Holders.Count > 0)
            text += " — Tutan: " + string.Join(", ", Holders.Select(h => $"{h.Name} ({h.Pid})"));
        var key = Id is null ? Path : $"{Path}|{Id}";
        return new ItemResult(key, Ok, $"{Status}: {text}".TrimEnd(' ', ':'), Bytes);
    }
}
