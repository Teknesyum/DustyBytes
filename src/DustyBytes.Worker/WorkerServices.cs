using System.Text.Json;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Scan.Duplicates;

namespace DustyBytes.Worker;

public sealed class WorkerServices
{
    public WorkerServices(SafetyGate gate, QuarantineOptions? quarantine = null)
    {
        Gate = gate;
        Recycle = new RecycleBin(gate);
        Quarantine = new QuarantineStore(gate, quarantine, Recycle);
        Delete = new DirectDelete(gate);
    }

    public SafetyGate Gate { get; }
    public QuarantineStore Quarantine { get; }
    public DirectDelete Delete { get; }
    public RecycleBin Recycle { get; }

    public static WorkerServices CreateDefault()
    {
        var gate = SafetyGate.LoadDefault();
        gate.List.LoadUserExceptions(Path.Combine(Paths.AppData, "exceptions.json"));
        return new WorkerServices(gate);
    }

    public const string TargetOnReboot = "on-reboot";
    public const string TargetExpired = Targets.Expired;
    public const string TargetAll = Targets.All;

    internal Task<WorkerResponse> ExecuteAsync(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct) =>
        request.Op switch
        {
            Ops.Ping => Task.FromResult(new WorkerResponse { Id = request.Id, Ok = true, Message = "pong", DryRun = DryRun.Enabled, Payload = Environment.ProcessId.ToString() }),
            Ops.Quarantine when IsDuplicate(request) => Task.Run(() => QuarantineDuplicates(request, progress, ct), ct),
            Ops.Quarantine => Task.Run(() => PerPath(request, progress, ct, Scoped(request, OpMethod.Quarantine, p => Quarantine.Quarantine(p, request.UnitId, request.IncludeUserData, request.SessionId))), ct),
            Ops.Delete when QuarantineOnly(request) => Task.FromResult(new WorkerResponse { Id = request.Id, Ok = false, Message = IsDuplicate(request) ? "Kopyalar yalnız karantinaya alınır; kalıcı silinmez" : "Eski indirmeler yalnız karantinaya alınır; kalıcı silinmez" }),
            Ops.Delete => Task.Run(() => PerPath(request, progress, ct, Scoped(request, OpMethod.Delete, p => Delete.Delete(p, request.IncludeUserData, request.Target == TargetOnReboot))), ct),
            Ops.Restore when IsSessionRestore(request) => Task.Run(() => RestoreSession(request, progress, ct), ct),
            Ops.Restore => Task.Run(() => PerId(request, progress, ct, Quarantine.Restore), ct),
            Ops.Purge => Task.Run(() => PurgeAsync(request, progress, ct), ct),
            Ops.ListQuarantine => Task.Run(() => ListQuarantine(request), ct),
            Ops.LockInfo => Task.Run(() => LockInfoOp(request), ct),
            Ops.Shutdown => Task.FromResult(new WorkerResponse { Id = request.Id, Ok = true, Message = "Worker kapanıyor" }),
            _ => throw new InvalidOperationException("Yerleşik olmayan işlem: " + request.Op),
        };

    static Func<string, OpResult> Scoped(WorkerRequest request, string method, Func<string, OpResult> action) =>
        request.Roots.Count == 0
            ? action
            : p => UnitScope.Refuse(p, request.Roots) is { } refusal ? OpResult.Denied(p, refusal, method) : action(p);

    public static bool QuarantineOnly(WorkerRequest request) =>
        request.UnitId?.StartsWith(nameof(Core.Model.UnitKind.OldDownload) + "-", StringComparison.Ordinal) == true || IsDuplicate(request);

    public static bool IsDuplicate(WorkerRequest request) =>
        request.UnitId?.StartsWith(nameof(Core.Model.UnitKind.Duplicate) + "-", StringComparison.Ordinal) == true;

    static bool IsSessionRestore(WorkerRequest request) =>
        request.SessionId is not null && request.Items.Count == 0 && request.Paths.Count == 0;

    WorkerResponse RestoreSession(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (!QuarantineStore.ValidSession(request.SessionId))
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "Geçersiz oturum kimliği" };
        var ids = Quarantine.SessionItems(request.SessionId!).Select(e => e.Id).ToList();
        return Run(request, progress, ct, ids, request.Op, Quarantine.Restore);
    }

    WorkerResponse QuarantineDuplicates(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        var guard = new DuplicateGuard(request.Target ?? "");
        string? first = null;
        var response = PerPath(request, progress, ct, p =>
        {
            if (guard.Check(p, ct) is { } refusal)
            {
                first ??= refusal;
                return OpResult.Failed(p, OpMethod.Quarantine, refusal);
            }
            return Quarantine.Quarantine(p, request.UnitId, request.IncludeUserData, request.SessionId);
        });
        return first is null ? response : response with { Message = response.Message + ": " + first };
    }

    public static PathTally Tally(int shown, IReadOnlyList<OpResult> results) => new()
    {
        Shown = shown,
        Processed = results.Count(r => r.Ok),
        ProcessedBytes = results.Where(r => r.Ok).Sum(r => r.Bytes),
        Vanished = results.Count(r => r.Status == OpStatus.NotFound),
        Protected = results.Count(r => r.Status == OpStatus.Denied),
        Locked = results.Count(r => r.Status == OpStatus.Locked),
        Failed = results.Count(r => r.Status is OpStatus.Failed or OpStatus.Conflict) + Math.Max(0, shown - results.Count),
    };

    static WorkerResponse Collect(WorkerRequest request, List<OpResult> results, int? shown = null)
    {
        var pending = results.Where(r => r.Status == OpStatus.Done && r.Method is OpMethod.Quarantine or OpMethod.RecycleBin).Sum(r => r.Bytes);
        var freed = results.Where(r => r.Status is OpStatus.Done or OpStatus.Scheduled or OpStatus.Locked or OpStatus.Failed && r.Method is OpMethod.Delete or OpMethod.Purge).Sum(r => r.Bytes);
        var failed = results.Count(r => !r.Ok);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = results.Count > 0 && failed == 0,
            DryRun = DryRun.Enabled || results.Any(r => r.IsDryRun),
            Message = results.Count == 0 ? "İşlenecek öğe yok" : failed == 0 ? $"{results.Count} öğe işlendi" : $"{results.Count - failed}/{results.Count} öğe işlendi",
            Items = [.. results.Select(r => r.ToItem())],
            PendingBytes = pending,
            FreedBytes = freed,
            Payload = JsonSerializer.Serialize(results, WorkerJson.Default.ListOpResult),
            Tally = Tally(shown ?? results.Count, results),
        };
    }

    static WorkerResponse Run(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct, IReadOnlyList<string> keys, string step, Func<string, OpResult> action)
    {
        var results = new List<OpResult>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new WorkerProgress(request.Id, step, keys.Count == 0 ? 100 : 100.0 * i / keys.Count, keys[i]));
            OpResult result;
            try
            {
                result = action(keys[i]);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                result = OpResult.Failed(keys[i], step, e.Message);
            }
            results.Add(result);
            progress.Report(new WorkerProgress(request.Id, step, 100.0 * (i + 1) / keys.Count, $"{result.Status}: {result.Path}"));
        }
        return Collect(request, results, keys.Count);
    }

    static WorkerResponse PerPath(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct, Func<string, OpResult> action) =>
        Run(request, progress, ct, request.Paths, request.Op, action);

    static WorkerResponse PerId(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct, Func<string, OpResult> action) =>
        Run(request, progress, ct, request.Items.Count > 0 ? request.Items : request.Paths, request.Op, action);

    WorkerResponse PurgeAsync(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (request.Items.Count == 0 && request.Target == TargetExpired)
        {
            progress.Report(new WorkerProgress(request.Id, request.Op, 0, "Süresi dolanlar"));
            return Collect(request, [.. Quarantine.PurgeExpired()]) with { Ok = true };
        }
        if (request.Items.Count == 0 && request.Target == TargetAll)
        {
            progress.Report(new WorkerProgress(request.Id, request.Op, 0, "Karantinanın tamamı"));
            var all = Quarantine.PurgeAll();
            return Collect(request, [.. all]) with { Ok = all.All(r => r.Ok) };
        }
        return PerId(request, progress, ct, Quarantine.Purge);
    }

    WorkerResponse ListQuarantine(WorkerRequest request)
    {
        var entries = Quarantine.List(includeClosed: request.Target == "all").ToList();
        var usage = Quarantine.Usage().ToList();
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            Message = $"{entries.Count} öğe",
            PendingBytes = usage.Sum(u => u.PendingBytes),
            Payload = JsonSerializer.Serialize(new QuarantineListing(entries, usage), WorkerJson.Default.QuarantineListing),
        };
    }

    static WorkerResponse LockInfoOp(WorkerRequest request)
    {
        var holders = LockInfo.Holders(request.Paths).ToList();
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            Message = holders.Count == 0 ? "Dosyayı tutan süreç yok" : $"{holders.Count} süreç tutuyor",
            Payload = JsonSerializer.Serialize(holders, WorkerJson.Default.ListLockHolder),
        };
    }
}
