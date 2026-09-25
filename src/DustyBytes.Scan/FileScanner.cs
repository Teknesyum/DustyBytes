using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DustyBytes.Core;

namespace DustyBytes.Scan;

public sealed class ScanSession
{
    internal ScanSession(ChannelReader<ScanNode[]> batches, Task<ScanResult> completion)
    {
        Batches = batches;
        Completion = completion;
    }

    public ChannelReader<ScanNode[]> Batches { get; }
    public Task<ScanResult> Completion { get; }
}

public sealed class FileScanner : IScanner
{
    public const int BatchSize = 1000;

    public Task<ScanResult> ScanAsync(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel) =>
        new FileScanRun(root, options, progress, cancel, stream: false).RunAsync();

    public ScanSession Start(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel)
    {
        var run = new FileScanRun(root, options, progress, cancel, stream: true);
        return new ScanSession(run.Batches!.Reader, run.RunAsync());
    }
}

internal sealed class FileScanRun
{
    private readonly record struct DirWork(ScanNode Node, string LongPath, string DisplayPath);

    private readonly string _display;
    private readonly string _long;
    private readonly string _volumeRoot;
    private readonly ScanOptions _options;
    private readonly IProgress<ScanProgress>? _progress;
    private readonly CancellationTokenSource _cts;
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly Channel<DirWork> _queue = Channel.CreateUnbounded<DirWork>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HardLinkIndex _links = new();
    private readonly long _cluster;
    private readonly long _usedBytes;
    private readonly bool _isVolumeRoot;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _batchLock = new();
    private List<ScanNode> _batch = new(FileScanner.BatchSize);
    private long _lastFlush;
    private long _lastReport;
    private long _pending;
    private long _files;
    private long _dirs;
    private long _bytes;
    private volatile bool _quit;
    private volatile string? _current;

    public Channel<ScanNode[]>? Batches { get; }

    public FileScanRun(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel, bool stream)
    {
        _display = Paths.Normalize(root);
        _long = Paths.ToLong(_display);
        _volumeRoot = Path.GetPathRoot(_display) ?? _display;
        _isVolumeRoot = _display.Equals(_volumeRoot, StringComparison.OrdinalIgnoreCase);
        _options = options;
        _progress = progress;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        foreach (var e in options.Excluded)
            _excluded.Add(Paths.Normalize(e));
        _excluded.Add(Path.Combine(_volumeRoot, Paths.QuarantineDir));
        _cluster = ClusterSize(_volumeRoot);
        if (Native.GetDiskFreeSpaceEx(_volumeRoot, out _, out var total, out var free))
            _usedBytes = (long)(total - free);
        if (stream)
            Batches = Channel.CreateUnbounded<ScanNode[]>(new UnboundedChannelOptions { SingleWriter = false });
    }

    internal static long ClusterSize(string volumeRoot)
    {
        var r = volumeRoot.EndsWith('\\') ? volumeRoot : volumeRoot + "\\";
        return Native.GetDiskFreeSpace(r, out var spc, out var bps, out _, out _) && spc * bps > 0 ? (long)spc * bps : 4096;
    }

    public async Task<ScanResult> RunAsync()
    {
        await Task.Yield();
        var cursor = UsnJournal.TryQueryCursor(_display);
        var rootNode = new ScanNode { Name = _display, IsDirectory = true };
        if (Native.GetFileAttributesEx(_long, 0, out var ad))
        {
            rootNode.LastWriteTicks = Native.FileTimeToTicks(ad.ftLastWriteTime);
            rootNode.Flags = AttrFlags(ad.dwFileAttributes);
        }
        else
        {
            var err = Marshal.GetLastPInvokeError();
            rootNode.Flags |= NodeFlags.Inaccessible;
            AddError(_display, err);
        }
        AddToBatch([rootNode]);

        var token = _cts.Token;
        var max = Math.Max(1, _options.MaxParallelism);
        using var gate = new SemaphoreSlim(max, max);
        if ((rootNode.Flags & NodeFlags.Inaccessible) == 0)
        {
            _pending = 1;
            _queue.Writer.TryWrite(new DirWork(rootNode, _long, _display));
            try
            {
                await foreach (var work in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Process(work, token);
                        }
                        catch (Exception ex)
                        {
                            _errors.Enqueue($"{work.DisplayPath}: {ex.Message}");
                        }
                        finally
                        {
                            gate.Release();
                            if (Interlocked.Decrement(ref _pending) == 0)
                                _queue.Writer.TryComplete();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            for (var i = 0; i < max; i++)
                await gate.WaitAsync().ConfigureAwait(false);
        }

        var cancelled = token.IsCancellationRequested || _quit;
        Report("Toplanıyor", 100, force: true);
        ScanTree.Aggregate(rootNode);
        FlushBatch(final: true);
        var (files, dirs) = ScanTree.Count(rootNode);
        Report("Tamamlandı", 100, force: true);
        _cts.Dispose();
        return new ScanResult
        {
            Root = rootNode,
            Files = files,
            Directories = dirs,
            Elapsed = _clock.Elapsed,
            Errors = _errors.ToArray(),
            Cancelled = cancelled,
            Method = _options.SmallBuffer ? "FindFirstFileEx" : "FindFirstFileEx+LargeFetch",
            Usn = cursor,
        };
    }

    private unsafe void Process(DirWork work, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;
        if (_options.Filter is { } filter)
        {
            var decision = filter(work.DisplayPath);
            if (decision == ScanDecision.Skip)
                return;
            if (decision == ScanDecision.Quit)
            {
                _quit = true;
                _cts.Cancel();
                return;
            }
        }

        _current = work.DisplayPath;
        var children = new List<ScanNode>();
        var subdirs = new List<DirWork>();
        long fileBytes = 0;
        long fileCount = 0;
        var pattern = work.LongPath.EndsWith('\\') ? work.LongPath + "*" : work.LongPath + "\\*";
        var displayPrefix = work.DisplayPath.EndsWith('\\') ? work.DisplayPath : work.DisplayPath + "\\";
        var longPrefix = work.LongPath.EndsWith('\\') ? work.LongPath : work.LongPath + "\\";
        Native.WIN32_FIND_DATAW data;
        var flags = _options.SmallBuffer ? 0u : Native.FIND_FIRST_EX_LARGE_FETCH;
        var h = Native.FindFirstFileEx(pattern, Native.FindExInfoBasic, &data, Native.FindExSearchNameMatch, 0, flags);
        if (h == Native.InvalidHandle)
        {
            var err = Marshal.GetLastPInvokeError();
            if (err != Native.ERROR_FILE_NOT_FOUND && err != Native.ERROR_NO_MORE_FILES)
            {
                work.Node.Flags |= NodeFlags.Inaccessible;
                AddError(work.DisplayPath, err);
            }
            return;
        }
        try
        {
            var n = 0;
            do
            {
                if ((++n & 255) == 0 && token.IsCancellationRequested)
                    break;
                var name = new string(data.cFileName);
                if (name is "" or "." or "..")
                    continue;
                if ((data.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                {
                    var display = displayPrefix + name;
                    if (_excluded.Contains(display))
                        continue;
                    var dir = CreateNode(&data, name, work.Node, longPrefix + name, _cluster, out var dtag);
                    children.Add(dir);
                    if (dtag == 0 || ReparseTags.ShouldEnter(dtag))
                        subdirs.Add(new DirWork(dir, longPrefix + name, display));
                    continue;
                }

                var node = CreateNode(&data, name, work.Node, longPrefix + name, _cluster, out var tag);
                var logical = node.LogicalSize;
                children.Add(node);
                if (logical > 0 && (node.Flags & NodeFlags.CloudPlaceholder) == 0 && (tag == 0 || tag == ReparseTags.Wof || tag == ReparseTags.Dedup)
                    && _links.IsDuplicate(node, logical, data.ftLastWriteTime, data.ftCreationTime))
                {
                    node.Flags |= NodeFlags.HardLinkDuplicate;
                    continue;
                }
                fileBytes += node.Size;
                fileCount++;
            }
            while (Native.FindNextFile(h, &data));
        }
        finally
        {
            Native.FindClose(h);
        }

        if (children.Count > 0)
        {
            children.TrimExcess();
            work.Node.Children = children;
        }
        Interlocked.Add(ref _files, fileCount);
        Interlocked.Add(ref _dirs, subdirs.Count);
        Interlocked.Add(ref _bytes, fileBytes);
        if (children.Count > 0)
            AddToBatch(children);
        if (!token.IsCancellationRequested)
        {
            foreach (var s in subdirs)
            {
                Interlocked.Increment(ref _pending);
                _queue.Writer.TryWrite(s);
            }
        }
        var pct = _isVolumeRoot && _usedBytes > 0 ? Math.Min(99.9, Interlocked.Read(ref _bytes) * 100.0 / _usedBytes) : -1;
        Report("Taranıyor", pct, force: false);
    }

    internal static unsafe ScanNode CreateNode(Native.WIN32_FIND_DATAW* data, string name, ScanNode? parent, string longPath, long cluster, out uint tag)
    {
        var attrs = data->dwFileAttributes;
        var isDir = (attrs & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
        var node = new ScanNode
        {
            Name = name,
            Parent = parent,
            IsDirectory = isDir,
            LastWriteTicks = Native.FileTimeToTicks(data->ftLastWriteTime),
            Flags = AttrFlags(attrs),
        };
        tag = 0;
        if ((attrs & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            tag = data->dwReserved0;
            node.Flags |= NodeFlags.ReparsePoint;
            node.ReparseTag = ReparseTags.Name(tag);
        }
        if (isDir)
        {
            if (tag == ReparseTags.MountPoint && IsVolumeMount(longPath))
                node.ReparseTag = "mount";
            node.NewestWriteTicks = node.LastWriteTicks;
            return node;
        }
        var logical = ((long)data->nFileSizeHigh << 32) | data->nFileSizeLow;
        node.LogicalSize = logical;
        if ((attrs & Native.CloudMask) != 0)
        {
            node.Flags |= NodeFlags.CloudPlaceholder;
            node.CloudSize = logical;
        }
        else if ((attrs & (Native.FILE_ATTRIBUTE_COMPRESSED | Native.FILE_ATTRIBUTE_SPARSE_FILE)) != 0 || tag == ReparseTags.Wof || tag == ReparseTags.Dedup)
        {
            if (tag == ReparseTags.Wof)
                node.Flags |= NodeFlags.Compressed;
            node.Size = CompressedSize(longPath, logical, cluster);
        }
        else
        {
            node.Size = RoundUp(logical, cluster);
        }
        node.NewestWriteTicks = node.LastWriteTicks;
        return node;
    }

    internal static long RoundUp(long logical, long cluster) => (logical + cluster - 1) / cluster * cluster;

    private static long CompressedSize(string longPath, long logical, long cluster)
    {
        var low = Native.GetCompressedFileSize(longPath, out var high);
        if (low == 0xFFFFFFFF && Marshal.GetLastPInvokeError() != 0)
            return RoundUp(logical, cluster);
        return ((long)high << 32) | low;
    }

    private static NodeFlags AttrFlags(uint attrs)
    {
        var f = NodeFlags.None;
        if ((attrs & Native.FILE_ATTRIBUTE_HIDDEN) != 0)
            f |= NodeFlags.Hidden;
        if ((attrs & Native.FILE_ATTRIBUTE_SYSTEM) != 0)
            f |= NodeFlags.System;
        if ((attrs & Native.FILE_ATTRIBUTE_COMPRESSED) != 0)
            f |= NodeFlags.Compressed;
        if ((attrs & Native.FILE_ATTRIBUTE_SPARSE_FILE) != 0)
            f |= NodeFlags.Sparse;
        if ((attrs & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            f |= NodeFlags.ReparsePoint;
        return f;
    }

    private static unsafe bool IsVolumeMount(string longPath)
    {
        var h = Native.CreateFile(longPath, 0, Native.FILE_SHARE_ALL, 0, Native.OPEN_EXISTING,
            Native.FILE_FLAG_BACKUP_SEMANTICS | Native.FILE_FLAG_OPEN_REPARSE_POINT, 0);
        if (h == Native.InvalidHandle)
            return false;
        try
        {
            var buf = stackalloc byte[16384];
            if (!Native.DeviceIoControl(h, Native.FSCTL_GET_REPARSE_POINT, null, 0, buf, 16384, out var got, 0) || got < 16)
                return false;
            if (*(uint*)buf != ReparseTags.MountPoint)
                return false;
            var off = *(ushort*)(buf + 8);
            var len = *(ushort*)(buf + 10);
            if (16 + off + len > got)
                return false;
            var sub = new string((char*)(buf + 16 + off), 0, len / 2);
            return sub.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private void AddError(string path, int err) =>
        _errors.Enqueue($"{path}: {Marshal.GetPInvokeErrorMessage(err)} ({err})");

    private void AddToBatch(IEnumerable<ScanNode> nodes)
    {
        if (Batches is null)
            return;
        lock (_batchLock)
        {
            foreach (var n in nodes)
            {
                _batch.Add(n);
                if (_batch.Count >= FileScanner.BatchSize)
                {
                    Batches.Writer.TryWrite(_batch.ToArray());
                    _batch.Clear();
                    _lastFlush = _clock.ElapsedMilliseconds;
                }
            }
            if (_batch.Count > 0 && _clock.ElapsedMilliseconds - _lastFlush > 250)
            {
                Batches.Writer.TryWrite(_batch.ToArray());
                _batch.Clear();
                _lastFlush = _clock.ElapsedMilliseconds;
            }
        }
    }

    private void FlushBatch(bool final)
    {
        if (Batches is null)
            return;
        lock (_batchLock)
        {
            if (_batch.Count > 0)
                Batches.Writer.TryWrite(_batch.ToArray());
            _batch = new List<ScanNode>(0);
            if (final)
                Batches.Writer.TryComplete();
        }
    }

    private void Report(string step, double pct, bool force)
    {
        if (_progress is null)
            return;
        var now = _clock.ElapsedMilliseconds;
        var last = Interlocked.Read(ref _lastReport);
        if (!force && (now - last < 100 || Interlocked.CompareExchange(ref _lastReport, now, last) != last))
            return;
        Interlocked.Exchange(ref _lastReport, now);
        _progress.Report(new ScanProgress(step, Interlocked.Read(ref _files), Interlocked.Read(ref _dirs), Interlocked.Read(ref _bytes), _current, pct));
    }
}

internal sealed unsafe class HardLinkIndex
{
    private const int ShardCount = 64;
    private readonly Dictionary<ulong, object>[] _shards;

    private sealed class Group
    {
        public required ScanNode First;
        public bool Resolved;
        public readonly HashSet<UInt128> Ids = [];
    }

    public HardLinkIndex()
    {
        _shards = new Dictionary<ulong, object>[ShardCount];
        for (var i = 0; i < ShardCount; i++)
            _shards[i] = new Dictionary<ulong, object>();
    }

    public int Opened;

    public bool IsDuplicate(ScanNode node, long logical, long write, long creation)
    {
        var key = Mix((ulong)logical, (ulong)write, (ulong)creation);
        var shard = _shards[(int)(key % ShardCount)];
        Group group;
        lock (shard)
        {
            if (!shard.TryGetValue(key, out var existing))
            {
                shard[key] = node;
                return false;
            }
            if (existing is Group g)
            {
                group = g;
            }
            else
            {
                group = new Group { First = (ScanNode)existing };
                shard[key] = group;
            }
        }
        lock (group)
        {
            if (!group.Resolved)
            {
                group.Resolved = true;
                if (Identity(group.First) is { } firstId)
                    group.Ids.Add(firstId);
            }
            if (Identity(node) is not { } id)
                return false;
            return !group.Ids.Add(id);
        }
    }

    private UInt128? Identity(ScanNode node)
    {
        Interlocked.Increment(ref Opened);
        var path = Paths.ToLong(node.FullPath);
        var h = Native.CreateFile(path, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_ALL, 0, Native.OPEN_EXISTING,
            Native.FILE_FLAG_BACKUP_SEMANTICS | Native.FILE_FLAG_OPEN_REPARSE_POINT, 0);
        if (h == Native.InvalidHandle)
            return null;
        try
        {
            if (!Native.GetFileInformationByHandle(h, out var info) || info.nNumberOfLinks < 2)
                return null;
            Native.FILE_ID_INFO id;
            if (Native.GetFileInformationByHandleEx(h, 18, &id, (uint)sizeof(Native.FILE_ID_INFO)))
                return new UInt128(id.IdHigh, id.IdLow);
            return new UInt128(info.dwVolumeSerialNumber, ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static ulong Mix(ulong a, ulong b, ulong c)
    {
        var h = a * 0x9E3779B97F4A7C15UL;
        h ^= b + 0xBF58476D1CE4E5B9UL + (h << 6) + (h >> 2);
        h ^= c + 0x94D049BB133111EBUL + (h << 6) + (h >> 2);
        h ^= h >> 31;
        return h;
    }
}
