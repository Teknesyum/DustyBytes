using System.Diagnostics;
using DustyBytes.Clean.Safety;
using DustyBytes.Clean.SpaceSaver;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Worker;

public sealed class SpaceHandlers(SafetyGate gate)
{
    const int MaxItems = 50;

    public Func<IEnumerable<string>, List<string>> Running { get; init; } = roots => RunningProcesses.Under(roots);
    public Func<string, string?> Unsupported { get; init; } = WofCompressor.Unsupported;
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
    public Func<string, CloudState?> Probe { get; init; } = CloudFiles.Probe;
    public Func<string, bool, int> SetPin { get; init; } = CloudFiles.SetPinState;
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(8);

    public Task<WorkerResponse> HandleCompress(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct) =>
        Task.Run(() => RunWof(request, progress, undo: false, ct), ct);

    public Task<WorkerResponse> HandleUncompress(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct) =>
        Task.Run(() => RunWof(request, progress, undo: true, ct), ct);

    public Task<WorkerResponse> HandleCloudFree(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct) =>
        Task.Run(() => CloudFree(request, progress, ct), ct);

    public Task<WorkerResponse> HandleCloudKeep(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct) =>
        Task.Run(() => CloudKeep(request, progress, ct), ct);

    WorkerResponse RunWof(WorkerRequest request, IProgress<WorkerProgress> progress, bool undo, CancellationToken ct)
    {
        WorkerResponse Fail(string message) => new() { Id = request.Id, Ok = false, Message = message };
        if (request.Paths.Count == 0)
            return Fail("Küçültülecek klasör yok");
        foreach (var path in request.Paths)
        {
            var verdict = gate.Check(path, GateOp.Compress, request.IncludeUserData);
            if (!verdict.Allowed)
                return Fail($"{path}: {verdict.Reason}");
            if (!Directory.Exists(Paths.ToLong(path)))
                return Fail($"Klasör bulunamadı: {path}");
            if (Unsupported(path) is { } reason)
                return Fail(reason);
        }
        var running = Running(request.Paths);
        if (running.Count > 0)
            return Fail("Program açık, önce kapat: " + string.Join(", ", running));

        progress.Report(new WorkerProgress(request.Id, "Dosyalar sayılıyor", -1, null));
        var files = undo
            ? CompressPlan.Compressed(request.Paths, gate.List.CheckGamePath, WofCompressor.IsCompressed, ct)
            : CompressPlan.Candidates(request.Paths, gate.List.CheckGamePath, ct);

        if (undo)
        {
            var grow = files.Sum(f => Math.Max(0, f.Length - Math.Max(0, WofCompressor.AllocatedBytes(f.Path))));
            var free = WofCompressor.FreeBytes(request.Paths[0]);
            if (grow > 0 && free < grow + 512L * 1024 * 1024)
                return Fail($"Geri almak için {Format.Bytes(grow)} boş yer gerekir; sürücüde {Format.Bytes(free)} var");
        }

        var step = undo ? "Küçültme geri alınıyor" : "Küçültülüyor";
        var dry = DryRun.Enabled;
        long before = 0, after = 0;
        int done = 0, skipped = 0, failed = 0;
        var items = new List<ItemResult>();
        var clock = Stopwatch.StartNew();
        var cancelled = false;
        for (var i = 0; i < files.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            var file = files[i];
            var sizeBefore = WofCompressor.AllocatedBytes(file.Path);
            if (sizeBefore < 0)
            {
                skipped++;
                continue;
            }
            if (dry)
            {
                DryRunLog.Write(undo ? Ops.Uncompress : Ops.Compress, file.Path, step);
                skipped++;
                continue;
            }
            var outcome = undo ? WofCompressor.Uncompress(file.Path) : WofCompressor.Compress(file.Path);
            var sizeAfter = WofCompressor.AllocatedBytes(file.Path);
            if (sizeAfter < 0)
                sizeAfter = sizeBefore;
            switch (outcome)
            {
                case WofOutcome.Done:
                    done++;
                    before += sizeBefore;
                    after += sizeAfter;
                    break;
                case WofOutcome.NotBeneficial:
                    skipped++;
                    break;
                default:
                    failed++;
                    if (items.Count < MaxItems)
                        items.Add(new ItemResult(file.Path, false, outcome == WofOutcome.Busy ? "Dosya kullanımda" : "İşlenemedi"));
                    break;
            }
            if (clock.ElapsedMilliseconds >= 250 || i == files.Count - 1)
            {
                clock.Restart();
                progress.Report(new WorkerProgress(request.Id, step, (i + 1) * 100.0 / files.Count, file.Path));
            }
        }

        var freed = Math.Max(0, before - after);
        var grown = Math.Max(0, after - before);
        var head = undo
            ? $"{done:N0} dosya eski haline döndü, {Format.Bytes(grown)} yer kullanıldı"
            : $"{done:N0} dosya küçültüldü, {Format.Bytes(freed)} yer açıldı";
        if (dry)
            head = $"Prova: {files.Count:N0} dosya işlenecekti";
        var tail = (skipped > 0 ? $", {skipped:N0} dosya atlandı" : "") + (failed > 0 ? $", {failed:N0} dosya işlenemedi" : "") + (cancelled ? ", durduruldu" : "");
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = done > 0 || failed == 0,
            DryRun = dry,
            Message = head + tail,
            FreedBytes = undo ? 0 : freed,
            PendingBytes = undo ? grown : 0,
            Items = items,
        };
    }

    WorkerResponse CloudFree(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (request.Paths.Count == 0)
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "Çevrimiçiye alınacak dosya yok" };
        var dry = DryRun.Enabled;
        var changed = new List<(string Path, long Before)>();
        var items = new List<ItemResult>();
        var skipped = 0;
        var now = UtcNow();
        for (var i = 0; i < request.Paths.Count; i++)
        {
            if (ct.IsCancellationRequested)
                break;
            var path = request.Paths[i];
            var reason = CloudReason(path, request.IncludeUserData, now);
            if (reason is not null)
            {
                skipped++;
                if (items.Count < MaxItems)
                    items.Add(new ItemResult(path, false, reason));
                continue;
            }
            if (dry)
            {
                DryRunLog.Write(Ops.CloudFree, path, "yalnız çevrimiçi yapılacaktı");
                continue;
            }
            var before = WofCompressor.AllocatedBytes(path);
            var result = SetPin(path, false);
            if (result != 0)
            {
                skipped++;
                if (items.Count < MaxItems)
                    items.Add(new ItemResult(path, false, $"OneDrive isteği kabul etmedi ({result:X8})"));
                continue;
            }
            changed.Add((path, Math.Max(0, before)));
            progress.Report(new WorkerProgress(request.Id, "Yalnız çevrimiçi yapılıyor", (i + 1) * 100.0 / request.Paths.Count, path));
        }

        var total = changed.Sum(c => c.Before);
        long freed = 0;
        if (changed.Count > 0)
        {
            progress.Report(new WorkerProgress(request.Id, "OneDrive yer açıyor", -1, null));
            var clock = Stopwatch.StartNew();
            do
            {
                freed = changed.Sum(c => Math.Max(0, c.Before - Math.Max(0, WofCompressor.AllocatedBytes(c.Path))));
                if (freed >= total || ct.IsCancellationRequested)
                    break;
                Thread.Sleep(500);
            }
            while (clock.Elapsed < SettleTime);
        }

        var message = dry
            ? $"Prova: {request.Paths.Count - skipped:N0} dosya yalnız çevrimiçi yapılacaktı"
            : $"{changed.Count:N0} dosya yalnız çevrimiçi; dosyalar OneDrive'da duruyor, açınca yeniden iner";
        if (skipped > 0)
            message += $", {skipped:N0} dosya atlandı";
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = changed.Count > 0 || dry,
            DryRun = dry,
            Message = message,
            FreedBytes = freed,
            PendingBytes = Math.Max(0, total - freed),
            Items = items,
        };
    }

    string? CloudReason(string path, bool includeUserData, DateTime now)
    {
        var verdict = gate.Check(path, GateOp.CloudFree, includeUserData);
        if (!verdict.Allowed)
            return verdict.Reason;
        FileInfo info;
        try
        {
            info = new FileInfo(Paths.ToLong(path));
            if (!info.Exists)
                return "Dosya bulunamadı";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Dosya okunamadı";
        }
        if (!CloudFiles.IsOldEnough(info.LastWriteTimeUtc, info.LastAccessTimeUtc, now))
            return "Dosya yakın zamanda açılmış";
        if (!CloudFiles.IsEligible(Probe(path), info.Length, gate.List.AttributeProvider(path) ?? info.Attributes))
            return "Dosya tam eşitlenmemiş ya da zaten çevrimiçi";
        return null;
    }

    WorkerResponse CloudKeep(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (request.Paths.Count == 0)
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "Tutulacak dosya yok" };
        var allowed = new List<string>();
        var items = new List<ItemResult>();
        foreach (var path in request.Paths)
        {
            var verdict = gate.Check(path, GateOp.CloudKeep, request.IncludeUserData);
            if (verdict.Allowed)
                allowed.Add(path);
            else if (items.Count < MaxItems)
                items.Add(new ItemResult(path, false, verdict.Reason));
        }
        var need = allowed.Sum(p =>
        {
            try
            {
                var info = new FileInfo(Paths.ToLong(p));
                return Math.Max(0, info.Length - Math.Max(0, WofCompressor.AllocatedBytes(p)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return 0L;
            }
        });
        if (allowed.Count > 0 && need > 0 && WofCompressor.FreeBytes(allowed[0]) < need + 512L * 1024 * 1024)
            return new WorkerResponse { Id = request.Id, Ok = false, Message = $"Geri indirmek için {Format.Bytes(need)} boş yer gerekir", Items = items };

        var dry = DryRun.Enabled;
        var done = 0;
        for (var i = 0; i < allowed.Count && !ct.IsCancellationRequested; i++)
        {
            if (dry)
            {
                DryRunLog.Write(Ops.CloudKeep, allowed[i], "bu cihazda tutulacaktı");
                continue;
            }
            var result = SetPin(allowed[i], true);
            if (result == 0)
                done++;
            else if (items.Count < MaxItems)
                items.Add(new ItemResult(allowed[i], false, $"OneDrive isteği kabul etmedi ({result:X8})"));
            progress.Report(new WorkerProgress(request.Id, "Bu cihazda tutuluyor", (i + 1) * 100.0 / allowed.Count, allowed[i]));
        }
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = done > 0 || dry,
            DryRun = dry,
            Message = dry ? $"Prova: {allowed.Count:N0} dosya bu cihazda tutulacaktı" : $"{done:N0} dosya bu cihazda tutulacak; OneDrive indiriyor",
            PendingBytes = need,
            Items = items,
        };
    }
}
