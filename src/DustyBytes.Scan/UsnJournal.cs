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

    internal static nint OpenRoot(string root)
    {
        var vol = VolumeOf(root) ?? throw new ArgumentException("Yalnız sürücü harfli yerel birimler desteklenir: " + root);
        var h = Native.CreateFile(vol + "\\", 0, Native.FILE_SHARE_ALL, 0, Native.OPEN_EXISTING, Native.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h == Native.InvalidHandle)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Sürücü kökü açılamadı: " + vol);
        return h;
    }

    internal static nint Open(string root, bool privileged) => privileged ? OpenVolume(root) : OpenRoot(root);

    public static UsnJournalInfo Query(string root)
    {
        var h = OpenRoot(root);
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

    public static List<UsnChange> ReadSince(string root, UsnCursor cursor, out UsnCursor next, CancellationToken cancel = default, bool privileged = false)
    {
        var h = Open(root, privileged);
        try
        {
            return ReadSince(h, cursor, out next, cancel, privileged);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static List<UsnChange> ReadSince(nint volume, UsnCursor cursor, out UsnCursor next, CancellationToken cancel, bool privileged)
    {
        var code = privileged ? Native.FSCTL_READ_USN_JOURNAL : Native.FSCTL_READ_UNPRIVILEGED_USN_JOURNAL;
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
                if (!Native.DeviceIoControl(volume, code, &req, (uint)sizeof(Native.READ_USN_JOURNAL_DATA_V1), buf, size, out var got, 0))
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
    private sealed record Listing(bool Gone, bool Failed, long Write, List<(ScanNode Node, uint Tag)> Entries)
    {
        public static readonly Listing Missing = new(true, false, 0, []);
        public static readonly Listing Unreadable = new(false, true, 0, []);
    }

    private static unsafe Listing List(string path, long cluster, string quarantine)
    {
        var longPath = Paths.ToLong(path);
        if (!Native.GetFileAttributesEx(longPath, 0, out var ad))
            return Marshal.GetLastPInvokeError() is Native.ERROR_FILE_NOT_FOUND or Native.ERROR_PATH_NOT_FOUND ? Listing.Missing : Listing.Unreadable;
        if ((ad.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) == 0)
            return Listing.Unreadable;
        var entries = new List<(ScanNode, uint)>();
        var longPrefix = longPath.EndsWith('\\') ? longPath : longPath + "\\";
        var displayPrefix = path.EndsWith('\\') ? path : path + "\\";
        Native.WIN32_FIND_DATAW data;
        var h = Native.FindFirstFileEx(longPrefix + "*", Native.FindExInfoBasic, &data, Native.FindExSearchNameMatch, 0, Native.FIND_FIRST_EX_LARGE_FETCH);
        if (h == Native.InvalidHandle)
        {
            var err = Marshal.GetLastPInvokeError();
            return err is Native.ERROR_FILE_NOT_FOUND or Native.ERROR_NO_MORE_FILES
                ? new Listing(false, false, Native.FileTimeToTicks(ad.ftLastWriteTime), entries)
                : Listing.Unreadable;
        }
        try
        {
            do
            {
                var name = new string(data.cFileName);
                if (name is "" or "." or "..")
                    continue;
                if ((data.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0 && (displayPrefix + name).Equals(quarantine, StringComparison.OrdinalIgnoreCase))
                    continue;
                var node = FileScanRun.CreateNode(&data, name, null, longPrefix + name, cluster, out var tag);
                entries.Add((node, tag));
            }
            while (Native.FindNextFile(h, &data));
        }
        finally
        {
            Native.FindClose(h);
        }
        return new Listing(false, false, Native.FileTimeToTicks(ad.ftLastWriteTime), entries);
    }

    private static bool Under(string path, HashSet<string> roots)
    {
        for (var p = Path.GetDirectoryName(path); p is not null; p = Path.GetDirectoryName(p))
            if (roots.Contains(p))
                return true;
        return false;
    }

    public static async Task<UsnUpdateResult> ApplyAsync(ScanResult cached, CancellationToken cancel = default, bool privileged = false)
    {
        var cursor = cached.Usn ?? throw new InvalidOperationException("Önbellekteki taramada USN konumu yok; tam tarama gerekir.");
        var root = cached.Root;
        var rootPath = root.Name;
        var volumeRoot = Path.GetPathRoot(rootPath)!;
        var cluster = FileScanRun.ClusterSize(volumeRoot);
        var quarantine = Path.Combine(volumeRoot, Paths.QuarantineDir);
        int added = 0, removed = 0, updated = 0, unresolved = 0;
        List<UsnChange> changes;
        UsnCursor next;
        var dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handle = UsnJournal.Open(rootPath, privileged);
        try
        {
            changes = UsnJournal.ReadSince(handle, cursor, out next, cancel, privileged);
            var seen = new HashSet<ulong>();
            foreach (var c in changes)
            {
                if (!seen.Add(c.ParentRef))
                    continue;
                cancel.ThrowIfCancellationRequested();
                var path = UsnJournal.ResolvePath(handle, c.ParentRef);
                if (path is null)
                {
                    unresolved++;
                    continue;
                }
                path = Paths.Normalize(path);
                if (Paths.IsUnder(path, rootPath) && !Paths.IsUnder(path, quarantine))
                    dirty.Add(path);
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }

        var order = dirty.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var listings = new Listing[order.Length];
        Parallel.For(0, order.Length, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = Environment.ProcessorCount },
            i => listings[i] = List(order[i], cluster, quarantine));

        var scanner = new FileScanner();
        var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Length; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var path = order[i];
            var listing = listings[i];
            if (Under(path, fresh) || root.Find(path) is not { IsDirectory: true } node)
                continue;
            if (listing.Gone)
            {
                if (node != root && node.Parent?.Children is { } siblings && siblings.Remove(node))
                    removed++;
                continue;
            }
            if (listing.Failed)
                continue;
            node.LastWriteTicks = listing.Write;
            var old = new Dictionary<string, ScanNode>(StringComparer.OrdinalIgnoreCase);
            if (node.Children is { } kids)
                foreach (var k in kids)
                    old.TryAdd(k.Name, k);
            var children = new List<ScanNode>(listing.Entries.Count);
            foreach (var (entry, tag) in listing.Entries)
            {
                if (old.Remove(entry.Name, out var existing) && existing.IsDirectory == entry.IsDirectory && existing.Name == entry.Name
                    && (!existing.IsDirectory || existing.ReparseTag == entry.ReparseTag))
                {
                    if (existing.LastWriteTicks != entry.LastWriteTicks || existing.LogicalSize != entry.LogicalSize || existing.Flags != entry.Flags)
                        updated++;
                    existing.LastWriteTicks = entry.LastWriteTicks;
                    existing.Flags = (entry.Flags & ~NodeFlags.HardLinkDuplicate) | (existing.Flags & (NodeFlags.HardLinkDuplicate | NodeFlags.Inaccessible));
                    existing.ReparseTag = entry.ReparseTag;
                    if (!existing.IsDirectory)
                    {
                        existing.Size = entry.Size;
                        existing.LogicalSize = entry.LogicalSize;
                        existing.CloudSize = entry.CloudSize;
                        existing.NewestWriteTicks = entry.NewestWriteTicks;
                    }
                    children.Add(existing);
                    continue;
                }
                if (existing is not null)
                    removed++;
                entry.Parent = node;
                if (entry.IsDirectory && (tag == 0 || ReparseTags.ShouldEnter(tag)))
                {
                    var full = Path.Combine(path, entry.Name);
                    var sub = await scanner.ScanAsync(full, new ScanOptions(), null, cancel).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();
                    entry.Flags |= sub.Root.Flags & NodeFlags.Inaccessible;
                    if (sub.Root.Children is { } subKids)
                    {
                        foreach (var k in subKids)
                            k.Parent = entry;
                        entry.Children = subKids;
                    }
                    fresh.Add(full);
                }
                children.Add(entry);
                added++;
            }
            removed += old.Count;
            children.TrimExcess();
            node.Children = children.Count > 0 ? children : null;
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
