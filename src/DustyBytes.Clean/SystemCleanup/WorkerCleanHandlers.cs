using System.Text.Json;
using DustyBytes.Clean.Rules;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class RealFileDeleter : ICleanupDeleter
{
    public bool DeleteFile(string path) => TryDeleteFile(path) == DeleteOutcome.Deleted;

    public DeleteOutcome TryDeleteFile(string path)
    {
        var target = Paths.ToLong(path);
        if (!File.Exists(target))
            return DeleteOutcome.Vanished;
        try
        {
            File.Delete(target);
            return DeleteOutcome.Deleted;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return DeleteOutcome.Vanished;
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33)
        {
            return DeleteOutcome.Locked;
        }
        catch (IOException)
        {
            return DeleteOutcome.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            return DeleteOutcome.Failed;
        }
    }

    public bool DeleteRegistryValue(string keyPath, string? valueName)
    {
        var split = RegistryPathResolver.Split(keyPath);
        if (split is null)
            return false;
        try
        {
            if (valueName is null)
            {
                var parentPath = Path.GetDirectoryName(split.Value.SubKey.Replace('\\', Path.DirectorySeparatorChar));
                var leaf = Path.GetFileName(split.Value.SubKey);
                using var parent = split.Value.Root.OpenSubKey(parentPath ?? "", writable: true);
                parent?.DeleteSubKeyTree(leaf, throwOnMissingSubKey: false);
            }
            else
            {
                using var key = split.Value.Root.OpenSubKey(split.Value.SubKey, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}

public static class WorkerCleanHandlers
{
    public static WorkerResponse HandleClean(
        WorkerRequest request,
        CleanerCatalog catalog,
        ICleanupDeleter deleter,
        IProgress<WorkerProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!request.UserApproved)
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "Kullanıcı onayı yok" };

        if (string.IsNullOrEmpty(request.Digest))
            return new WorkerResponse { Id = request.Id, Ok = false, DryRun = DryRun.Enabled, Message = "Önizleme yok; temizlik yalnız gösterilen listeyle yapılır" };

        if (!string.Equals(PreviewDigest.Of(request.Paths), request.Digest, StringComparison.OrdinalIgnoreCase))
            return new WorkerResponse { Id = request.Id, Ok = false, DryRun = DryRun.Enabled, Message = "Gönderilen liste önizleme özetiyle tutmuyor; temizlik yapılmadı" };

        progress?.Report(new WorkerProgress(request.Id, "Temizlik", -1, $"{Core.Model.Format.Count(request.Paths.Count)} gösterilen dosya"));
        var run = catalog.Execute(CleanerCatalog.Selection(request.Items), request.Paths, deleter, ct);
        var results = run.Options;

        var items = results.Select(r => new ItemResult(
            $"{r.RuleId}/{r.OptionId}",
            r.Ran,
            r.Ran ? $"{r.DeletedFiles} dosya, {Core.Model.Format.Bytes(r.DeletedBytes)}" : r.SkipReason ?? "Atlandı",
            r.DeletedBytes)).ToList();

        var summary = run.Tally.Describe(verb: DryRun.Enabled ? "silinecek" : "silinen");

        return new WorkerResponse
        {
            Id = request.Id,
            Ok = run.Error is null,
            DryRun = DryRun.Enabled,
            Items = items,
            FreedBytes = run.Tally.ProcessedBytes,
            Tally = run.Tally,
            Message = run.Error ?? (DryRun.Enabled ? "Prova kipi: silme yapılmadı. " + summary : summary),
        };
    }

    public static WorkerResponse HandleCleanPreview(WorkerRequest request, CleanerCatalog catalog, CancellationToken ct = default)
    {
        var preview = catalog.Plan(CleanerCatalog.Selection(request.Items), ct).ToPreview();
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            DryRun = DryRun.Enabled,
            Message = $"{Core.Model.Format.Count(preview.Count)} dosya, {Core.Model.Format.Bytes(preview.Bytes)}",
            Payload = JsonSerializer.Serialize(preview, IpcJson.Default.CleanPreview),
        };
    }

    public static async Task<WorkerResponse> HandleSystemClean(
        WorkerRequest request,
        IReadOnlyList<ISystemCleanupTask> tasks,
        IProgress<WorkerProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!request.UserApproved)
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "Kullanıcı onayı yok" };

        var targets = request.Items.Count == 0
            ? tasks.Where(t => !t.ExplicitOnly).Select(t => (Task: t, Id: t.Id)).ToList()
            : tasks
                .SelectMany(t => new[] { t.Id }.Concat(t.ExtraIds).Select(id => (Task: t, Id: id)))
                .Where(p => request.Items.Contains(p.Id))
                .ToList();

        var items = new List<ItemResult>();
        long freed = 0;

        foreach (var (task, id) in targets)
        {
            var lineProgress = new Progress<string>(line =>
                progress?.Report(new WorkerProgress(request.Id, task.Name, 0, line)));

            var result = id == task.Id
                ? await task.RunAsync(lineProgress, ct)
                : await task.RunAsync(id, lineProgress, ct);
            items.Add(new ItemResult(id, result.Ok, result.Message, result.FreedBytes));
            freed += result.FreedBytes;
        }

        return new WorkerResponse
        {
            Id = request.Id,
            Ok = items.All(i => i.Ok),
            DryRun = DryRun.Enabled,
            Items = items,
            FreedBytes = freed,
            Message = DryRun.Enabled ? "Prova kipi: yürütülmedi" : "Sistem temizliği tamamlandı",
        };
    }
}
