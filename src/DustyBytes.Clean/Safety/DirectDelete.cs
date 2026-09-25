using DustyBytes.Core;

namespace DustyBytes.Clean.Safety;

public sealed class DirectDelete
{
    const int MaxHolderQueries = 16;

    readonly SafetyGate _gate;

    public DirectDelete(SafetyGate gate)
    {
        _gate = gate;
    }

    public OpResult Delete(string path, bool includeUserData = false, bool scheduleLockedOnReboot = false)
    {
        var verdict = _gate.Check(path, includeUserData);
        if (!verdict.Allowed)
            return OpResult.Denied(path, verdict, OpMethod.Delete);
        var plain = Paths.Normalize(path);
        if (!FileTree.Exists(plain))
            return OpResult.NotFound(plain, OpMethod.Delete);

        if (DryRun.Enabled)
        {
            var (bytes, count) = FileTree.Measure(plain);
            DryRunLog.Write(OpMethod.Delete, plain, $"{count} öğe, {bytes} bayt silinecekti");
            return new OpResult
            {
                Path = plain,
                Status = OpStatus.DryRun,
                Method = OpMethod.Delete,
                Bytes = bytes,
                Message = $"Prova: {count} öğe silinecekti",
            };
        }

        var report = FileTree.Delete(plain, _gate.List.ProtectedNameReason);
        if (report.Complete)
            return new OpResult
            {
                Path = plain,
                Status = OpStatus.Done,
                Method = OpMethod.Delete,
                Bytes = report.RemovedBytes,
                Message = $"{report.RemovedCount} öğe silindi",
            };

        var holders = report.Locked.Count > 0 ? LockInfo.TryHolders(report.Locked.Take(MaxHolderQueries)).ToList() : [];
        var skipped = report.Locked.Concat(report.Failed).Concat(report.Protected).ToList();

        if (scheduleLockedOnReboot && report.Locked.Count > 0)
        {
            var scheduled = 0;
            foreach (var file in report.Locked)
                if (LockInfo.ScheduleDeleteOnReboot(file).Ok)
                    scheduled++;
            foreach (var dir in report.Remaining.OrderByDescending(d => d.Length))
                LockInfo.ScheduleDeleteOnReboot(dir);
            return new OpResult
            {
                Path = plain,
                Status = OpStatus.Scheduled,
                Method = OpMethod.Delete,
                Bytes = report.RemovedBytes,
                Holders = holders,
                Skipped = skipped,
                Message = $"{report.RemovedCount} öğe silindi, {scheduled} kilitli öğe yeniden başlatmaya ertelendi",
            };
        }

        return new OpResult
        {
            Path = plain,
            Status = report.Locked.Count > 0 ? OpStatus.Locked : OpStatus.Failed,
            Method = OpMethod.Delete,
            Bytes = report.RemovedBytes,
            Holders = holders,
            Skipped = skipped,
            Message = $"{report.RemovedCount} öğe silindi; {report.Locked.Count} kilitli, {report.Failed.Count} erişilemeyen, {report.Protected.Count} korumalı öğe atlandı",
        };
    }
}
