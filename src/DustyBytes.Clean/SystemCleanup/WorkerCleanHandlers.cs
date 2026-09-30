using DustyBytes.Clean.Rules;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class RealFileDeleter : ICleanupDeleter
{
    public bool DeleteFile(string path)
    {
        try
        {
            File.Delete(Paths.ToLong(path));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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

        var selection = request.Items
            .Select(item => item.Split('/', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0])
            .Select(g => new RuleSelection(g.Key, [.. g.Select(p => p[1])]))
            .ToList();

        var results = catalog.Execute(selection, deleter);

        var items = results.Select(r => new ItemResult(
            $"{r.RuleId}/{r.OptionId}",
            r.Ran,
            r.Ran ? $"{r.DeletedFiles} dosya, {Core.Model.Format.Bytes(r.DeletedBytes)}" : r.SkipReason ?? "Atlandı",
            r.DeletedBytes)).ToList();

        var freed = results.Sum(r => r.DeletedBytes);

        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            DryRun = DryRun.Enabled,
            Items = items,
            FreedBytes = freed,
            Message = DryRun.Enabled ? "Prova kipi: silme yapılmadı" : "Temizlik tamamlandı",
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
