using System.ComponentModel;
using System.Runtime.InteropServices;
using DustyBytes.Core;

namespace DustyBytes.Scan;

public sealed record UsnCursor(ulong JournalId, long NextUsn);

public sealed record UsnJournalInfo(ulong JournalId, long FirstUsn, long NextUsn, long LowestValidUsn, ulong MaximumSize);

public readonly record struct UsnChange(ulong FileRef, ulong ParentRef, long Usn, uint Reason, uint Attributes, string Name, long TimeTicks)
{
    public bool IsDirectory => (Attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
}

public sealed class UsnJournalResetException(string message) : Exception(message);

public static unsafe class UsnJournal
{
    public const uint ReasonFileCreate = 0x100;
    public const uint ReasonFileDelete = 0x200;
    public const uint ReasonRenameOldName = 0x1000;
    public const uint ReasonRenameNewName = 0x2000;
    public const uint ReasonClose = 0x80000000;

    public static string? VolumeOf(string root)
    {
        var full = Paths.Normalize(root);
        return full.Length >= 2 && full[1] == ':' && char.IsAsciiLetter(full[0]) ? char.ToUpperInvariant(full[0]) + ":" : null;
    }

    internal static nint OpenVolume(string root)
    {
        var vol = VolumeOf(root) ?? throw new ArgumentException("Yalnız sürücü harfli yerel birimler desteklenir: " + root);
        var h = Native.CreateFile(@"\\.\" + vol, Native.GENERIC_READ, Native.FILE_SHARE_ALL, 0, Native.OPEN_EXISTING, 0, 0);
        if (h == Native.InvalidHandle)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Birim açılamadı: " + vol);
        return h;
    }

    public static UsnJournalInfo Query(string root)
    {
        var h = OpenVolume(root);
        try
        {
            return Query(h);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static UsnJournalInfo Query(nint volume)
    {
        Native.USN_JOURNAL_DATA_V0 d;
        if (!Native.DeviceIoControl(volume, Native.FSCTL_QUERY_USN_JOURNAL, null, 0, &d, (uint)sizeof(Native.USN_JOURNAL_DATA_V0), out _, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "USN günlüğü sorgulanamadı");
        return new UsnJournalInfo(d.UsnJournalID, d.FirstUsn, d.NextUsn, d.LowestValidUsn, d.MaximumSize);
    }

    public static UsnCursor? TryQueryCursor(string root)
    {
        if (VolumeOf(root) is null)
            return null;
        try
        {
            var q = Query(root);
            return new UsnCursor(q.JournalId, q.NextUsn);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static List<UsnChange> ReadSince(string root, UsnCursor cursor, out UsnCursor next, CancellationToken cancel = default)
    {
        var h = OpenVolume(root);
        try
        {
            return ReadSince(h, cursor, out next, cancel);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static List<UsnChange> ReadSince(nint volume, UsnCursor cursor, out UsnCursor next, CancellationToken cancel)
    {
        var info = Query(volume);
        if (info.JournalId != cursor.JournalId)
            throw new UsnJournalResetException("USN günlüğü yeniden oluşturulmuş; tam tarama gerekir.");
        if (cursor.NextUsn < info.FirstUsn)
            throw new UsnJournalResetException("USN günlüğünde kayıp var; tam tarama gerekir.");

        var list = new List<UsnChange>();
        const int size = 1 << 16;
        var buf = (byte*)NativeMemory.Alloc(size);
        try
        {
            var start = cursor.NextUsn;
            while (start < info.NextUsn)
            {
                cancel.ThrowIfCancellationRequested();
                var req = new Native.READ_USN_JOURNAL_DATA_V1
                {
                    StartUsn = start,
                    ReasonMask = 0xFFFFFFFF,
                    UsnJournalID = cursor.JournalId,
                    MinMajorVersion = 2,
                    MaxMajorVersion = 2,
                };
                if (!Native.DeviceIoControl(volume, Native.FSCTL_READ_USN_JOURNAL, &req, (uint)sizeof(Native.READ_USN_JOURNAL_DATA_V1), buf, size, out var got, 0))
                {
                    var err = Marshal.GetLastPInvokeError();
                    if (err is Native.ERROR_JOURNAL_ENTRY_DELETED or Native.ERROR_JOURNAL_NOT_ACTIVE or Native.ERROR_JOURNAL_DELETE_IN_PROGRESS)
                        throw new UsnJournalResetException("USN günlüğü okunamadı (" + err + "); tam tarama gerekir.");
                    throw new Win32Exception(err, "USN günlüğü okunamadı");
                }
                var nextUsn = *(long*)buf;
                if (got <= 8 || nextUsn == start)
                    break;
                var off = 8u;
                while (off + 60 <= got)
                {
                    var rec = buf + off;
                    var len = *(uint*)rec;
                    if (len == 0 || off + len > got)
                        break;
                    if (*(ushort*)(rec + 4) == 2)
                    {
                        var usn = *(long*)(rec + 24);
                        if (usn >= info.NextUsn)
                            break;
                        var nameLen = *(ushort*)(rec + 56);
                        var nameOff = *(ushort*)(rec + 58);
                        list.Add(new UsnChange(
                            *(ulong*)(rec + 8),
                            *(ulong*)(rec + 16),
                            usn,
                            *(uint*)(rec + 40),
                            *(uint*)(rec + 52),
                            new string((char*)(rec + nameOff), 0, nameLen / 2),
                            Native.FileTimeToTicks(*(long*)(rec + 32))));
                    }
                    off += len;
                }
                start = nextUsn;
            }
        }
        finally
        {
            NativeMemory.Free(buf);
        }
        next = new UsnCursor(info.JournalId, info.NextUsn);
        return list;
    }

    internal static string? ResolvePath(nint volume, ulong fileRef)
    {
        var desc = new Native.FILE_ID_DESCRIPTOR { dwSize = (uint)sizeof(Native.FILE_ID_DESCRIPTOR), Type = 0, FileId = (long)fileRef };
        var h = Native.OpenFileById(volume, ref desc, 0, Native.FILE_SHARE_ALL, 0, Native.FILE_FLAG_BACKUP_SEMANTICS);
        if (h == Native.InvalidHandle)
            return null;
        try
        {
            const int cap = 32768;
            var buf = (char*)NativeMemory.Alloc(cap * sizeof(char));
            try
            {
                var n = Native.GetFinalPathNameByHandle(h, buf, cap, 0);
                return n == 0 || n >= cap ? null : Paths.FromLong(new string(buf, 0, (int)n));
            }
            finally
            {
                NativeMemory.Free(buf);
            }
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}

public sealed record UsnUpdateResult(ScanResult Result, int Changes, int Added, int Removed, int Updated, int Unresolved);

public static class UsnUpdater
{
    private static unsafe ScanNode? Stat(string full, ScanNode? parent, long cluster, out uint tag)
    {
        tag = 0;
        Native.WIN32_FIND_DATAW data;
        var longPath = Paths.ToLong(full);
        var h = Native.FindFirstFileEx(longPath, Native.FindExInfoBasic, &data, Native.FindExSearchNameMatch, 0, 0);
        if (h == Native.InvalidHandle)
            return null;
        Native.FindClose(h);
        return FileScanRun.CreateNode(&data, Path.GetFileName(full), parent, longPath, cluster, out tag);
    }

    public static async Task<UsnUpdateResult> ApplyAsync(ScanResult cached, CancellationToken cancel = default)
    {
        var cursor = cached.Usn ?? throw new InvalidOperationException("Önbellekteki taramada USN konumu yok; tam tarama gerekir.");
        var root = cached.Root;
        var rootPath = root.Name;
        var volume = UsnJournal.OpenVolume(rootPath);
        var cluster = FileScanRun.ClusterSize(Path.GetPathRoot(rootPath)!);
        var quarantine = Path.Combine(Path.GetPathRoot(rootPath)!, Paths.QuarantineDir);
        int added = 0, removed = 0, updated = 0, unresolved = 0;
        List<UsnChange> changes;
        UsnCursor next;
        var targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            changes = UsnJournal.ReadSince(volume, cursor, out next, cancel);
            var parents = new Dictionary<ulong, string?>();
            foreach (var c in changes)
            {
                if (!parents.TryGetValue(c.ParentRef, out var parentPath))
                {
                    parentPath = UsnJournal.ResolvePath(volume, c.ParentRef);
                    parents[c.ParentRef] = parentPath;
                }
                if (parentPath is null)
                {
                    unresolved++;
                    continue;
                }
                var full = Path.Combine(parentPath, c.Name);
                if (!Paths.IsUnder(full, rootPath) || Paths.IsUnder(full, quarantine))
                    continue;
                targets[full] = true;
            }
        }
        finally
        {
            Native.CloseHandle(volume);
        }

        var scanner = new FileScanner();
        foreach (var full in targets.Keys.OrderBy(p => p.Length))
        {
            cancel.ThrowIfCancellationRequested();
            var existing = root.Find(full);
            var parentPath = Path.GetDirectoryName(full);
            var parent = parentPath is null ? null : root.Find(parentPath);
            var node = Stat(full, parent, cluster, out var tag);
            if (node is null)
            {
                if (existing is not null && existing != root && existing.Parent?.Children is { } siblings)
                {
                    siblings.Remove(existing);
                    removed++;
                }
                continue;
            }
            if (parent is null)
                continue;
            if (existing is not null && existing.IsDirectory == node.IsDirectory)
            {
                existing.LastWriteTicks = node.LastWriteTicks;
                existing.Flags = (node.Flags & ~NodeFlags.HardLinkDuplicate) | (existing.Flags & NodeFlags.HardLinkDuplicate);
                existing.ReparseTag = node.ReparseTag;
                if (!existing.IsDirectory)
                {
                    existing.Size = node.Size;
                    existing.LogicalSize = node.LogicalSize;
                    existing.CloudSize = node.CloudSize;
                }
                updated++;
                continue;
            }
            if (existing is not null)
            {
                parent.Children?.Remove(existing);
                removed++;
            }
            if (node.IsDirectory && (tag == 0 || ReparseTags.ShouldEnter(tag)))
            {
                var sub = await scanner.ScanAsync(full, new ScanOptions(), null, cancel).ConfigureAwait(false);
                if (sub.Root.Children is { } kids)
                {
                    foreach (var k in kids)
                        k.Parent = node;
                    node.Children = kids;
                }
            }
            (parent.Children ??= []).Add(node);
            added++;
        }

        ScanTree.Aggregate(root);
        var (files, dirs) = ScanTree.Count(root);
        var result = new ScanResult
        {
            Root = root,
            Files = files,
            Directories = dirs,
            Elapsed = cached.Elapsed,
            FinishedAt = DateTimeOffset.UtcNow,
            Errors = cached.Errors,
            Cancelled = cached.Cancelled,
            Method = cached.Method,
            Usn = next,
        };
        return new UsnUpdateResult(result, changes.Count, added, removed, updated, unresolved);
    }
}
