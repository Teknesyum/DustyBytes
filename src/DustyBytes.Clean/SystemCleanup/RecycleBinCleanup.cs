using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;

namespace DustyBytes.Clean.SystemCleanup;

public sealed record RecycleInfo(long Size, DateTime DeletedUtc, string OriginalPath);

public sealed record RecycleEntry(string InfoPath, string DataPath, long Size, DateTime DeletedUtc, string OriginalPath);

public static class RecycleInfoFile
{
    const int MaxInfoBytes = 64 * 1024;

    public static RecycleInfo? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24)
            return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(data);
        var size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        var fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        string original;
        if (version == 1)
        {
            var tail = data[24..Math.Min(data.Length, 24 + 520)];
            original = Encoding.Unicode.GetString(tail[..(tail.Length & ~1)]);
        }
        else if (version == 2)
        {
            if (data.Length < 28)
                return null;
            var chars = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
            if (chars < 0 || 28L + chars * 2L > data.Length)
                return null;
            original = Encoding.Unicode.GetString(data.Slice(28, chars * 2));
        }
        else
        {
            return null;
        }

        var nul = original.IndexOf('\0');
        if (nul >= 0)
            original = original[..nul];
        if (size < 0 || fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc())
            return null;
        return new RecycleInfo(size, DateTime.FromFileTimeUtc(fileTime), original);
    }

    public static IReadOnlyList<RecycleEntry> Scan(string binDir)
    {
        var list = new List<RecycleEntry>();
        if (!Directory.Exists(binDir))
            return list;
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        IEnumerable<string> infos;
        try
        {
            infos = Directory.EnumerateFiles(binDir, "$I*", options).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return list;
        }
        foreach (var info in infos)
        {
            var name = Path.GetFileName(info);
            if (name.Length <= 2)
                continue;
            try
            {
                var fileInfo = new FileInfo(info);
                if (fileInfo.Length > MaxInfoBytes)
                    continue;
                if (Parse(File.ReadAllBytes(info)) is not { } parsed)
                    continue;
                list.Add(new RecycleEntry(info, Path.Combine(binDir, "$R" + name[2..]), parsed.Size, parsed.DeletedUtc, parsed.OriginalPath));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return list;
    }

    public static IReadOnlyList<RecycleEntry> Select(IEnumerable<RecycleEntry> entries, DateTime nowUtc, bool old) =>
        [.. entries.Where(e => (nowUtc - e.DeletedUtc >= RecycleBinCleanup.Age) == old)];
}

public sealed partial class RecycleBinCleanup : ISystemCleanupTask
{
    public const string OldId = "recycle-bin-old";
    public const string RecentId = "recycle-bin-recent";
    public static readonly TimeSpan Age = TimeSpan.FromDays(30);

    readonly SafetyGate _gate;
    readonly string? _sid;
    readonly bool _old;
    readonly Func<IEnumerable<string>> _bins;
    readonly Func<DateTime> _now;
    readonly Func<long?> _total;

    public RecycleBinCleanup(SafetyGate gate, string? userSid, bool old, Func<IEnumerable<string>>? bins = null, Func<DateTime>? nowUtc = null, Func<long?>? total = null)
    {
        _gate = gate;
        _sid = userSid;
        _old = old;
        _bins = bins ?? (() => DefaultBins(userSid));
        _now = nowUtc ?? (() => DateTime.UtcNow);
        _total = total ?? QueryTotal;
    }

    public string Id => _old ? OldId : RecentId;
    public string Name => _old ? "Geri Dönüşüm Kutusu · 30 günden eski" : "Geri Dönüşüm Kutusu · son 30 gün";
    public bool ExplicitOnly => true;

    public static IEnumerable<string> DefaultBins(string? sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            yield break;
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }
        foreach (var drive in drives)
        {
            bool ready;
            try
            {
                ready = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch (IOException)
            {
                ready = false;
            }
            if (ready)
                yield return Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
        }
    }

    IReadOnlyList<RecycleEntry> Entries() => [.. _bins().SelectMany(RecycleInfoFile.Scan)];

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var all = Entries();
        var old = RecycleInfoFile.Select(all, _now(), true);
        var oldBytes = old.Sum(e => e.Size);
        var total = _total() is { } t && t >= oldBytes ? t : all.Sum(e => e.Size);
        if (_old)
        {
            var detail = old.Count == 0
                ? "30 günden önce silinmiş öğe yok"
                : $"{old.Count} öğe 30 günden önce silinmiş. Bunları zaten sildiniz; kutuda yalnız yer tutuyorlar.";
            return Task.FromResult(new SystemCleanupEstimate(Id, oldBytes, oldBytes > 0, detail));
        }
        var recentCount = all.Count - old.Count;
        var recentBytes = Math.Max(0, total - oldBytes);
        var recentDetail = recentCount == 0 && recentBytes == 0
            ? "Son 30 günde silinmiş öğe yok"
            : $"Son 30 günde silinen {recentCount} öğe. Geri almak isteyebileceğiniz için seçili gelmez.";
        return Task.FromResult(new SystemCleanupEstimate(Id, recentBytes, false, recentDetail, Silent: false));
    }

    public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_sid))
            return Task.FromResult(new SystemCleanupResult(Id, false, "Kullanıcı kimliği bilinmiyor; Geri Dönüşüm Kutusu boşaltılmadı", 0));
        if (!_gate.SystemOpAllowed(SafetyGate.RecycleEmptyOp))
            return Task.FromResult(new SystemCleanupResult(Id, false, "Korumalı liste Geri Dönüşüm Kutusu boşaltmaya izin vermiyor", 0));

        var targets = RecycleInfoFile.Select(Entries(), _now(), _old);
        long freed = 0;
        long planned = 0;
        var done = 0;
        var skipped = 0;
        foreach (var entry in targets)
        {
            ct.ThrowIfCancellationRequested();
            var data = _gate.CheckRecycleEntry(entry.DataPath, _sid);
            var info = _gate.CheckRecycleEntry(entry.InfoPath, _sid);
            if (!data.Allowed || !info.Allowed)
            {
                skipped++;
                progress.Report($"Atlandı: {entry.OriginalPath} ({(data.Allowed ? info.Reason : data.Reason)})");
                continue;
            }
            if (DryRun.Enabled)
            {
                DryRunLog.Write(OpMethod.Purge, entry.OriginalPath, "Geri Dönüşüm Kutusu'ndan kalıcı silinecekti");
                progress.Report($"[prova] {entry.OriginalPath}");
                planned += entry.Size;
                done++;
                continue;
            }
            var complete = true;
            if (FileTree.Exists(entry.DataPath))
            {
                var report = FileTree.Delete(entry.DataPath, _gate.List.ProtectedNameReason);
                freed += report.RemovedBytes;
                complete = report.Complete;
            }
            if (!complete)
            {
                skipped++;
                progress.Report($"Tamamı silinemedi: {entry.OriginalPath}");
                continue;
            }
            FileTree.Delete(entry.InfoPath, _gate.List.ProtectedNameReason);
            done++;
        }

        if (DryRun.Enabled)
            return Task.FromResult(new SystemCleanupResult(Id, true, $"Prova kipi: {done} öğe silinecekti ({Core.Model.Format.Bytes(planned)})", 0));
        var message = skipped == 0
            ? $"{done} öğe Geri Dönüşüm Kutusu'ndan silindi"
            : $"{done} öğe silindi, {skipped} öğe atlandı";
        return Task.FromResult(new SystemCleanupResult(Id, skipped == 0 || done > 0, message, freed));
    }

    static long? QueryTotal()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        try
        {
            return SHQueryRecycleBin(null, ref info) >= 0 ? info.i64Size : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);
}
