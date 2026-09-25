namespace DustyBytes.Scan.Tests;

[Collection("scan")]
public sealed class FileScannerTests(ScanFixture fx)
{
    private static readonly ScanOptions Opts = new();

    private Task<ScanResult> Scan(ScanOptions? o = null, CancellationToken cancel = default) =>
        new FileScanner().ScanAsync(fx.Root, o ?? Opts, null, cancel);

    [Fact]
    public async Task SizesAndCountsAggregate()
    {
        var r = await Scan(new ScanOptions { Excluded = [fx.Denied] });
        var root = r.Root;
        Assert.False(r.Cancelled);
        var file1 = root.Find(Path.Combine(fx.Root, @"a\file1.bin"))!;
        Assert.Equal(5000, file1.LogicalSize);
        Assert.Equal(ScanFixture.RoundUp(5000), file1.Size);
        var a = root.Child("a")!;
        Assert.Equal(3, a.FileCount);
        Assert.Equal(ScanFixture.RoundUp(5000) + ScanFixture.RoundUp(10000) + ScanFixture.RoundUp(1), a.Size);
        Assert.Equal(5000 + 10000 + 1, a.LogicalSize);

        var counted = root.Descendants().Where(n => !n.IsDirectory && (n.Flags & NodeFlags.HardLinkDuplicate) == 0 && !IsUnderJunction(n)).ToList();
        Assert.Equal(counted.Sum(n => n.Size), root.Size);
        Assert.Equal(counted.Count, root.FileCount);
        Assert.Equal(6, root.FileCount);
        Assert.Equal(ScanFixture.Newest.Ticks, root.NewestWriteTicks);
        Assert.Equal(ScanFixture.Newest.Ticks, a.NewestWriteTicks);
    }

    private static bool IsUnderJunction(ScanNode n)
    {
        for (var p = n.Parent; p is not null; p = p.Parent)
            if ((p.Flags & NodeFlags.ReparsePoint) != 0)
                return true;
        return false;
    }

    [Fact]
    public async Task LongPathIsScanned()
    {
        Assert.True(fx.LongFile.Length > 260);
        var r = await Scan();
        var node = r.Root.Find(fx.LongFile);
        Assert.NotNull(node);
        Assert.Equal(3000, node.LogicalSize);
        Assert.Equal(fx.LongFile, node.FullPath);
    }

    [Fact]
    public async Task JunctionIsNotEntered()
    {
        Assert.True(fx.JunctionMade);
        var r = await Scan();
        var j = r.Root.Child("j")!;
        Assert.True((j.Flags & NodeFlags.ReparsePoint) != 0);
        Assert.Equal("junction", j.ReparseTag);
        Assert.Null(j.Children);
        Assert.Equal(0, j.Size);
    }

    [Fact]
    public async Task HardLinkCountedOnce()
    {
        Assert.True(fx.HardLinkMade);
        var r = await Scan();
        var h1 = r.Root.Child("h1.bin")!;
        var h2 = r.Root.Child("h2.bin")!;
        var dups = new[] { h1, h2 }.Count(n => (n.Flags & NodeFlags.HardLinkDuplicate) != 0);
        Assert.Equal(1, dups);
        var others = r.Root.Children!.Where(n => n != h1 && n != h2).Sum(n => n.Size);
        Assert.Equal(others + ScanFixture.RoundUp(8192), r.Root.Size);
    }

    [Fact]
    public async Task CompressedFileUsesCompressedSize()
    {
        if (!fx.Compressed)
            return;
        var r = await Scan();
        var c = r.Root.Child("comp.bin")!;
        Assert.True((c.Flags & NodeFlags.Compressed) != 0);
        Assert.Equal(1 << 20, c.LogicalSize);
        Assert.True(c.Size < c.LogicalSize, $"Size {c.Size}");
    }

    [Fact]
    public async Task InaccessibleDirectoryIsFlagged()
    {
        ScanResult r;
        using (fx.LockDenied())
            r = await Scan();
        var d = r.Root.Child("denied")!;
        Assert.True((d.Flags & NodeFlags.Inaccessible) != 0);
        Assert.Contains(r.Errors, e => e.StartsWith(fx.Denied, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SkipFilterPrunesBranch()
    {
        var r = await Scan(new ScanOptions { Filter = p => p.EndsWith(@"\a\b", StringComparison.OrdinalIgnoreCase) ? ScanDecision.Skip : ScanDecision.Continue });
        var a = r.Root.Child("a")!;
        Assert.Equal(1, a.FileCount);
        Assert.Null(a.Child("b")!.Children);
        Assert.False(r.Cancelled);
    }

    [Fact]
    public async Task QuitFilterStops()
    {
        var r = await Scan(new ScanOptions { Filter = p => p.EndsWith(@"\a", StringComparison.OrdinalIgnoreCase) ? ScanDecision.Quit : ScanDecision.Continue });
        Assert.True(r.Cancelled);
    }

    [Fact]
    public async Task CancellationReturnsPartial()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await Scan(cancel: cts.Token);
        Assert.True(r.Cancelled);
        Assert.Equal(0, r.Root.FileCount);
    }

    [Fact]
    public async Task ExcludedPathIsLeftOut()
    {
        var r = await Scan(new ScanOptions { Excluded = [Path.Combine(fx.Root, "a")] });
        Assert.Null(r.Root.Child("a"));
    }

    [Fact]
    public async Task SmallBufferMatches()
    {
        var big = await Scan();
        var small = await Scan(new ScanOptions { SmallBuffer = true });
        Assert.Equal(big.Root.Size, small.Root.Size);
        Assert.Equal(big.Files, small.Files);
    }

    [Fact]
    public async Task StreamDeliversEveryNode()
    {
        var session = new FileScanner().Start(fx.Root, new ScanOptions { MaxParallelism = 2 }, null, default);
        var total = 0;
        var biggest = 0;
        await foreach (var batch in session.Batches.ReadAllAsync())
        {
            total += batch.Length;
            biggest = Math.Max(biggest, batch.Length);
        }
        var r = await session.Completion;
        Assert.Equal(1 + r.Files + r.Directories, total);
        Assert.True(biggest <= FileScanner.BatchSize);
    }

    [Fact]
    public async Task ProgressReportsCompletion()
    {
        var steps = new List<ScanProgress>();
        var progress = new SyncProgress(steps.Add);
        await new FileScanner().ScanAsync(fx.Root, Opts, progress, default);
        Assert.Equal("Dosyalar sayıldı, sonuç hazırlanıyor", steps[^1].Step);
    }

    private sealed class SyncProgress(Action<ScanProgress> a) : IProgress<ScanProgress>
    {
        private readonly object _lock = new();
        public void Report(ScanProgress value)
        {
            lock (_lock)
                a(value);
        }
    }
}
