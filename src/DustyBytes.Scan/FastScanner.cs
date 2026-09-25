using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DustyBytes.Core;

namespace DustyBytes.Scan;

public sealed class FastScanner : IScanner
{
    public const int ChunkSize = 8 << 20;

    public Task<ScanResult> ScanAsync(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel) =>
        Task.Run(() => new MftScanRun(root, options, progress, cancel).Run());

    public static bool IsSupported(string root)
    {
        var vol = UsnJournal.VolumeOf(root);
        if (vol is null)
            return false;
        try
        {
            return new DriveInfo(vol).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal sealed unsafe class MftScanRun
{
    private const uint Signature = 0x454C4946;
    private const int RootRecord = 5;
    private const int FirstUserRecord = 24;

    private readonly string _display;
    private readonly ScanOptions _options;
    private readonly IProgress<ScanProgress>? _progress;
    private readonly CancellationToken _cancel;
    private readonly List<string> _errors = [];
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastReport = -1000;

    private long _cluster;
    private int _recordSize;
    private long _records;

    private uint[] _parent = [];
    private string?[] _name = [];
    private byte[] _rank = [];
    private byte[] _state = [];
    private long[] _alloc = [];
    private long[] _logical = [];
    private long[] _write = [];
    private uint[] _attrs = [];
    private uint[] _tag = [];
    private readonly List<(int Record, uint Parent, string Name)> _extraLinks = [];
    private long _attrListRecords;
    private long _attrListLost;
    private long _badRecords;

    private const byte InUse = 1;
    private const byte IsDir = 2;
    private const byte HasData = 4;
    private const byte HasAttrList = 8;
    private const byte IsBase = 16;

    public MftScanRun(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel)
    {
        _display = Paths.Normalize(root);
        _options = options;
        _progress = progress;
        _cancel = cancel;
        foreach (var e in options.Excluded)
            _excluded.Add(Paths.Normalize(e));
        _excluded.Add(Path.Combine(Path.GetPathRoot(_display)!, Paths.QuarantineDir));
    }

    public ScanResult Run()
    {
        var volume = UsnJournal.OpenVolume(_display);
        UsnCursor? cursor = null;
        var cancelled = false;
        try
        {
            try
            {
                var q = UsnJournal.Query(volume);
                cursor = new UsnCursor(q.JournalId, q.NextUsn);
            }
            catch (Win32Exception ex)
            {
                _errors.Add("USN günlüğü: " + ex.Message);
            }
            cancelled = !ReadMft(volume);
        }
        finally
        {
            Native.CloseHandle(volume);
        }

        var rootNode = cancelled ? new ScanNode { Name = _display, IsDirectory = true } : BuildTree(out cancelled);
        Report("Toplanıyor", 95, true);
        ScanTree.Aggregate(rootNode);
        var (files, dirs) = ScanTree.Count(rootNode);
        if (_attrListRecords > 0)
            _errors.Add($"$ATTRIBUTE_LIST taşıyan kayıt: {_attrListRecords}, $DATA bulunamayan: {_attrListLost}");
        if (_badRecords > 0)
            _errors.Add($"Fixup doğrulaması tutmayan kayıt: {_badRecords}");
        Report("Tamamlandı", 100, true);
        return new ScanResult
        {
            Root = rootNode,
            Files = files,
            Directories = dirs,
            Elapsed = _clock.Elapsed,
            Errors = _errors,
            Cancelled = cancelled,
            Method = "MFT",
            Usn = cursor,
        };
    }

    private static void ReadAt(nint h, long offset, byte* buf, int len)
    {
        if (!Native.SetFilePointerEx(h, offset, out _, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Birimde konumlanamadı");
        var done = 0;
        while (done < len)
        {
            if (!Native.ReadFile(h, buf + done, (uint)(len - done), out var got, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Birim okunamadı");
            if (got == 0)
                throw new EndOfStreamException("Birim beklenenden kısa");
            done += (int)got;
        }
    }

    private bool ReadMft(nint volume)
    {
        var buf = (byte*)NativeMemory.AlignedAlloc(FastScanner.ChunkSize, 4096);
        try
        {
            ReadAt(volume, 0, buf, 4096);
            if (*(ulong*)(buf + 3) != 0x202020205346544EUL)
                throw new NotSupportedException("Birim NTFS değil: " + _display);
            var bytesPerSector = *(ushort*)(buf + 0x0B);
            int spc = buf[0x0D];
            if (spc > 0x80)
                spc = 1 << (256 - spc);
            _cluster = (long)bytesPerSector * spc;
            var mftLcn = *(long*)(buf + 0x30);
            var cpr = (sbyte)buf[0x40];
            _recordSize = cpr > 0 ? (int)(cpr * _cluster) : 1 << -cpr;

            var head = (int)Math.Max(4096, _recordSize);
            ReadAt(volume, mftLcn * _cluster, buf, head);
            if (!Fixup(buf))
                throw new InvalidDataException("MFT kayıt 0 bozuk");
            var runs = new List<(long Lcn, long Length)>();
            long mftSize = 0;
            if (!MftDataRuns(buf, runs, ref mftSize))
                throw new InvalidDataException("MFT $DATA çalışma listesi okunamadı");
            ExtendMftRuns(volume, buf, runs);
            var covered = runs.Sum(r => r.Length) * _cluster;
            if (covered < mftSize)
                _errors.Add($"MFT çalışma listesi eksik: {covered} / {mftSize} bayt");

            _records = mftSize / _recordSize;
            Allocate((int)_records);

            long recordIndex = 0;
            foreach (var (lcn, length) in runs)
            {
                var runBytes = length * _cluster;
                long done = 0;
                while (done < runBytes && recordIndex < _records)
                {
                    if (_cancel.IsCancellationRequested)
                        return false;
                    var take = (int)Math.Min(FastScanner.ChunkSize, runBytes - done);
                    take -= take % _recordSize;
                    if (take <= 0)
                        break;
                    if (lcn < 0)
                    {
                        recordIndex += take / _recordSize;
                        done += take;
                        continue;
                    }
                    ReadAt(volume, lcn * _cluster + done, buf, take);
                    var count = take / _recordSize;
                    for (var i = 0; i < count && recordIndex < _records; i++, recordIndex++)
                        ParseRecord(buf + (long)i * _recordSize, (int)recordIndex);
                    done += take;
                    Report("MFT okunuyor", recordIndex * 90.0 / _records, false);
                }
            }
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
        }
        return true;
    }

    private void ExtendMftRuns(nint volume, byte* rec0, List<(long Lcn, long Length)> runs)
    {
        var off = *(ushort*)(rec0 + 0x14);
        List<(long Vcn, long Record)>? segments = null;
        while (off + 16 <= _recordSize)
        {
            var a = rec0 + off;
            var type = *(uint*)a;
            if (type == 0xFFFFFFFF)
                break;
            var len = *(uint*)(a + 4);
            if (len == 0)
                break;
            if (type == 0x20)
            {
                if (a[8] == 0)
                {
                    segments = ListSegments(a + *(ushort*)(a + 0x14), *(uint*)(a + 0x10));
                    break;
                }
                var size = *(long*)(a + 0x30);
                if (size <= 0 || size > 1 << 20)
                {
                    _errors.Add($"$MFT $ATTRIBUTE_LIST boyutu beklenmedik: {size}");
                    return;
                }
                var listRuns = new List<(long, long)>();
                DecodeRuns(a + *(ushort*)(a + 0x20), a + len, listRuns);
                var data = ReadRuns(volume, listRuns, (int)size);
                fixed (byte* d = data)
                    segments = ListSegments(d, (uint)data.Length);
                break;
            }
            off += (ushort)len;
        }
        if (segments is null)
            return;
        segments.Sort();

        var block = Math.Max(4096, _recordSize);
        var raw = (byte*)NativeMemory.AlignedAlloc((nuint)block, 4096);
        try
        {
            foreach (var (vcn, record) in segments)
            {
                if (vcn == 0)
                    continue;
                var at = RecordOffset(runs, record);
                if (at < 0)
                {
                    _errors.Add($"$MFT uzantı kaydı {record} konumlanamadı");
                    continue;
                }
                var aligned = at / block * block;
                ReadAt(volume, aligned, raw, block);
                var one = raw + (at - aligned);
                if (!Fixup(one))
                {
                    _errors.Add($"$MFT uzantı kaydı {record} bozuk");
                    continue;
                }
                var o = *(ushort*)(one + 0x14);
                while (o + 16 <= _recordSize)
                {
                    var a = one + o;
                    var type = *(uint*)a;
                    if (type == 0xFFFFFFFF)
                        break;
                    var len = *(uint*)(a + 4);
                    if (len == 0)
                        break;
                    if (type == 0x80 && a[9] == 0 && a[8] != 0 && *(long*)(a + 0x10) == vcn)
                    {
                        DecodeRuns(a + *(ushort*)(a + 0x20), a + len, runs);
                        break;
                    }
                    o += (ushort)len;
                }
            }
        }
        finally
        {
            NativeMemory.AlignedFree(raw);
        }
    }

    private static List<(long Vcn, long Record)> ListSegments(byte* list, uint listLen)
    {
        var segments = new List<(long Vcn, long Record)>();
        for (uint p = 0; p + 0x1A <= listLen;)
        {
            var e = list + p;
            var elen = *(ushort*)(e + 4);
            if (elen == 0)
                break;
            var seg = (long)(*(ulong*)(e + 0x10) & 0xFFFFFFFFFFFFUL);
            if (*(uint*)e == 0x80 && e[6] == 0 && seg != 0)
                segments.Add((*(long*)(e + 8), seg));
            p += elen;
        }
        return segments;
    }

    private byte[] ReadRuns(nint volume, List<(long Lcn, long Length)> runs, int size)
    {
        var result = new byte[size];
        var filled = 0;
        var chunk = (int)Math.Max(4096, _cluster);
        var buf = (byte*)NativeMemory.AlignedAlloc((nuint)chunk, 4096);
        try
        {
            foreach (var (lcn, length) in runs)
            {
                for (long c = 0; c < length && filled < size; c += chunk / _cluster)
                {
                    var take = (int)Math.Min(Math.Min(chunk, (length - c) * _cluster), size - filled);
                    if (lcn >= 0)
                    {
                        ReadAt(volume, (lcn + c) * _cluster, buf, chunk);
                        new ReadOnlySpan<byte>(buf, take).CopyTo(result.AsSpan(filled));
                    }
                    filled += take;
                }
            }
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
        }
        return result;
    }

    private long RecordOffset(List<(long Lcn, long Length)> runs, long record)
    {
        var bytes = record * _recordSize;
        foreach (var (lcn, length) in runs)
        {
            var runBytes = length * _cluster;
            if (bytes < runBytes)
                return lcn < 0 ? -1 : lcn * _cluster + bytes;
            bytes -= runBytes;
        }
        return -1;
    }

    private void Allocate(int n)
    {
        _parent = new uint[n];
        _name = new string?[n];
        _rank = new byte[n];
        _state = new byte[n];
        _alloc = new long[n];
        _logical = new long[n];
        _write = new long[n];
        _attrs = new uint[n];
        _tag = new uint[n];
    }

    private bool Fixup(byte* rec)
    {
        if (*(uint*)rec != Signature)
            return false;
        var usaOff = *(ushort*)(rec + 4);
        var usaCount = *(ushort*)(rec + 6);
        if (usaCount < 2 || usaOff + usaCount * 2 > _recordSize)
            return false;
        var stride = _recordSize / (usaCount - 1);
        var usn = *(ushort*)(rec + usaOff);
        for (var k = 1; k < usaCount; k++)
        {
            var tail = (ushort*)(rec + k * stride - 2);
            if (*tail != usn)
                return false;
            *tail = *(ushort*)(rec + usaOff + k * 2);
        }
        return true;
    }

    private bool MftDataRuns(byte* rec, List<(long, long)> runs, ref long size)
    {
        var off = *(ushort*)(rec + 0x14);
        while (off + 16 <= _recordSize)
        {
            var a = rec + off;
            var type = *(uint*)a;
            if (type == 0xFFFFFFFF)
                break;
            var len = *(uint*)(a + 4);
            if (len == 0)
                break;
            if (type == 0x80 && a[9] == 0 && a[8] != 0)
            {
                size = *(long*)(a + 0x30);
                DecodeRuns(a + *(ushort*)(a + 0x20), a + len, runs);
                return runs.Count > 0;
            }
            off += (ushort)len;
        }
        return false;
    }

    private static void DecodeRuns(byte* p, byte* end, List<(long, long)> runs)
    {
        long lcn = 0;
        while (p < end && *p != 0)
        {
            var header = *p++;
            var lenBytes = header & 0xF;
            var offBytes = header >> 4;
            long length = 0;
            for (var i = 0; i < lenBytes; i++)
                length |= (long)p[i] << (8 * i);
            p += lenBytes;
            if (offBytes == 0)
            {
                runs.Add((-1, length));
                continue;
            }
            long delta = 0;
            for (var i = 0; i < offBytes; i++)
                delta |= (long)p[i] << (8 * i);
            if (offBytes < 8 && (p[offBytes - 1] & 0x80) != 0)
                delta |= -1L << (8 * offBytes);
            p += offBytes;
            lcn += delta;
            runs.Add((lcn, length));
        }
    }

    private void ParseRecord(byte* rec, int index)
    {
        if (*(uint*)rec != Signature)
            return;
        if (!Fixup(rec))
        {
            _badRecords++;
            return;
        }
        var flags = *(ushort*)(rec + 0x16);
        if ((flags & 1) == 0)
            return;
        var baseRef = *(ulong*)(rec + 0x20) & 0xFFFFFFFFFFFFUL;
        var target = baseRef == 0 ? index : (int)baseRef;
        if ((uint)target >= (uint)_state.Length)
            return;
        if (baseRef == 0)
        {
            _state[target] |= InUse | IsBase;
            if ((flags & 2) != 0)
                _state[target] |= IsDir;
        }

        var off = *(ushort*)(rec + 0x14);
        var used = Math.Min(*(uint*)(rec + 0x18), (uint)_recordSize);
        while (off + 16 <= used)
        {
            var a = rec + off;
            var type = *(uint*)a;
            if (type == 0xFFFFFFFF)
                break;
            var len = *(uint*)(a + 4);
            if (len < 16 || off + len > used)
                break;
            var nonResident = a[8] != 0;
            var nameLen = a[9];
            switch (type)
            {
                case 0x10 when !nonResident:
                {
                    var v = a + *(ushort*)(a + 0x14);
                    if (*(uint*)(a + 0x10) >= 36)
                    {
                        _write[target] = Native.FileTimeToTicks(*(long*)(v + 8));
                        _attrs[target] = *(uint*)(v + 32);
                    }
                    break;
                }
                case 0x20:
                    _state[target] |= HasAttrList;
                    break;
                case 0x30 when !nonResident:
                {
                    var v = a + *(ushort*)(a + 0x14);
                    var parent = (uint)(*(ulong*)v & 0xFFFFFFFFFFFFUL);
                    var chars = v[0x40];
                    var ns = v[0x41];
                    byte rank = ns switch { 1 or 3 => 3, 0 => 2, _ => 1 };
                    if (rank > _rank[target])
                    {
                        if (_name[target] is { } oldName && _rank[target] >= 2)
                            _extraLinks.Add((target, _parent[target], oldName));
                        _rank[target] = rank;
                        _parent[target] = parent;
                        _name[target] = new string((char*)(v + 0x42), 0, chars);
                    }
                    else if (rank >= 2)
                    {
                        var name = new string((char*)(v + 0x42), 0, chars);
                        if (parent != _parent[target] || !name.Equals(_name[target], StringComparison.OrdinalIgnoreCase))
                            _extraLinks.Add((target, parent, name));
                    }
                    break;
                }
                case 0x80:
                {
                    var unnamed = nameLen == 0;
                    var wof = !unnamed && nameLen == 17 && new ReadOnlySpan<char>(a + *(ushort*)(a + 0x0A), nameLen).SequenceEqual("WofCompressedData");
                    if (!unnamed && !wof)
                        break;
                    if (!nonResident)
                    {
                        if (unnamed)
                        {
                            _logical[target] = *(uint*)(a + 0x10);
                            _state[target] |= HasData;
                        }
                        break;
                    }
                    if (*(long*)(a + 0x10) != 0)
                        break;
                    var attrFlags = *(ushort*)(a + 0x0C);
                    var allocated = (attrFlags & 0x8001) != 0 ? *(long*)(a + 0x40) : *(long*)(a + 0x28);
                    _alloc[target] += allocated;
                    if (unnamed)
                    {
                        _logical[target] = *(long*)(a + 0x30);
                        _state[target] |= HasData;
                    }
                    break;
                }
                case 0xC0 when !nonResident:
                    if (*(uint*)(a + 0x10) >= 4)
                        _tag[target] = *(uint*)(a + *(ushort*)(a + 0x14));
                    break;
            }
            off += (ushort)len;
        }
    }

    private int FindRecord(string path, int[] first, int[] next, int[] entryRecord, string[] entryName)
    {
        var root = Path.GetPathRoot(path)!;
        var rest = path[root.Length..];
        var current = RootRecord;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var found = -1;
            for (var e = first[current]; e >= 0; e = next[e])
            {
                if (entryName[e].Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    found = entryRecord[e];
                    break;
                }
            }
            if (found < 0)
                return -1;
            current = found;
        }
        return current;
    }

    private ScanNode BuildTree(out bool cancelled)
    {
        cancelled = false;
        var n = _state.Length;
        var entries = n + _extraLinks.Count;
        var entryRecord = new int[entries];
        var entryName = new string[entries];
        var entryParent = new int[entries];
        var first = new int[n];
        var next = new int[entries];
        Array.Fill(first, -1);
        var e = 0;
        for (var r = 0; r < n; r++)
        {
            if ((_state[r] & (InUse | IsBase)) != (InUse | IsBase) || _name[r] is null)
                continue;
            if ((_state[r] & HasAttrList) != 0)
            {
                _attrListRecords++;
                if ((_state[r] & (HasData | IsDir)) == 0)
                    _attrListLost++;
            }
            entryRecord[e] = r;
            entryName[e] = _name[r]!;
            entryParent[e] = (int)_parent[r];
            e++;
        }
        foreach (var (rec, parent, name) in _extraLinks)
        {
            if ((_state[rec] & InUse) == 0)
                continue;
            entryRecord[e] = rec;
            entryName[e] = name;
            entryParent[e] = (int)parent;
            e++;
        }
        for (var i = e - 1; i >= 0; i--)
        {
            var p = entryParent[i];
            var r = entryRecord[i];
            if ((uint)p >= (uint)n || p == r)
                continue;
            if (p == RootRecord && r < FirstUserRecord)
                continue;
            next[i] = first[p];
            first[p] = i;
        }
        Array.Resize(ref entryName, e);

        var target = FindRecord(_display, first, next, entryRecord, entryName);
        if (target < 0)
            throw new DirectoryNotFoundException("MFT'de bulunamadı: " + _display);

        var rootNode = new ScanNode { Name = _display, IsDirectory = true, LastWriteTicks = _write[target] };
        var seen = new bool[n];
        seen[target] = true;
        var stack = new Stack<(ScanNode Node, int Record, string Path)>();
        stack.Push((rootNode, target, _display));
        long files = 0, dirs = 0, bytes = 0;
        while (stack.Count > 0)
        {
            if (_cancel.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            var (node, rec, path) = stack.Pop();
            if (_options.Filter is { } filter)
            {
                var d = filter(path);
                if (d == ScanDecision.Skip)
                    continue;
                if (d == ScanDecision.Quit)
                {
                    cancelled = true;
                    break;
                }
            }
            var prefix = path.EndsWith('\\') ? path : path + "\\";
            List<ScanNode>? kids = null;
            for (var i = first[rec]; i >= 0; i = next[i])
            {
                var r = entryRecord[i];
                var name = entryName[i];
                var isDir = (_state[r] & IsDir) != 0;
                if (isDir && _excluded.Contains(prefix + name))
                    continue;
                var child = new ScanNode
                {
                    Name = name,
                    Parent = node,
                    IsDirectory = isDir,
                    LastWriteTicks = _write[r],
                    Flags = Flags(r),
                };
                if (_tag[r] != 0)
                {
                    child.Flags |= NodeFlags.ReparsePoint;
                    child.ReparseTag = ReparseTags.Name(_tag[r]);
                }
                (kids ??= []).Add(child);
                if (isDir)
                {
                    child.NewestWriteTicks = child.LastWriteTicks;
                    if (seen[r])
                        continue;
                    seen[r] = true;
                    dirs++;
                    if (_tag[r] == 0 || ReparseTags.ShouldEnter(_tag[r]))
                        stack.Push((child, r, prefix + name));
                    continue;
                }
                child.LogicalSize = _logical[r];
                child.NewestWriteTicks = child.LastWriteTicks;
                if (ReparseTags.IsCloud(_tag[r]) && ((_attrs[r] & Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 || (_alloc[r] == 0 && _logical[r] > 0)))
                {
                    child.Flags |= NodeFlags.CloudPlaceholder;
                    child.CloudSize = _logical[r];
                }
                else
                {
                    child.Size = _alloc[r];
                }
                if (_tag[r] == ReparseTags.Wof)
                    child.Flags |= NodeFlags.Compressed;
                if (seen[r])
                {
                    child.Flags |= NodeFlags.HardLinkDuplicate;
                    continue;
                }
                seen[r] = true;
                files++;
                bytes += child.Size;
            }
            if (kids is not null)
            {
                kids.TrimExcess();
                node.Children = kids;
            }
            if (_progress is not null && _clock.ElapsedMilliseconds - _lastReport >= 100)
            {
                _lastReport = _clock.ElapsedMilliseconds;
                _progress.Report(new ScanProgress("Ağaç kuruluyor", files, dirs, bytes, path, 90));
            }
        }
        return rootNode;
    }

    private NodeFlags Flags(int r)
    {
        var a = _attrs[r];
        var f = NodeFlags.None;
        if ((a & Native.FILE_ATTRIBUTE_HIDDEN) != 0)
            f |= NodeFlags.Hidden;
        if ((a & Native.FILE_ATTRIBUTE_SYSTEM) != 0)
            f |= NodeFlags.System;
        if ((a & Native.FILE_ATTRIBUTE_COMPRESSED) != 0)
            f |= NodeFlags.Compressed;
        if ((a & Native.FILE_ATTRIBUTE_SPARSE_FILE) != 0)
            f |= NodeFlags.Sparse;
        return f;
    }

    private void Report(string step, double pct, bool force)
    {
        if (_progress is null)
            return;
        var now = _clock.ElapsedMilliseconds;
        if (!force && now - _lastReport < 100)
            return;
        _lastReport = now;
        _progress.Report(new ScanProgress(step, 0, 0, 0, _display, pct));
    }
}
