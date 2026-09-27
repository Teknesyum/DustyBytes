using Xunit.Abstractions;

namespace DustyBytes.Scan.Tests;

[Collection("scan")]
public sealed class IndexAndMftTests(ScanFixture fx, ITestOutputHelper output)
{
    [Fact]
    public async Task IndexRoundTrip()
    {
        var db = Path.Combine(Path.GetTempPath(), "dustybytes-index-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        try
        {
            var r = await new FileScanner().ScanAsync(fx.Root, new ScanOptions(), null, default);
            var index = new ScanIndex(db);
            index.Save(r);
            index.Save(r);
            var seen = new List<(long Read, long Total)>();
            var back = index.Load(fx.Root, (read, total) => seen.Add((read, total)))!;
            Assert.NotEmpty(seen);
            Assert.Equal(seen[^1].Read, seen[^1].Total);
            Assert.Equal(r.Files + r.Directories + 1, seen[^1].Total);
            Assert.Equal(r.Root.Size, back.Root.Size);
            Assert.Equal(r.Root.FileCount, back.Root.FileCount);
            Assert.Equal(r.Files, back.Files);
            Assert.Equal(r.Directories, back.Directories);
            Assert.Equal(r.Usn, back.Usn);
            Assert.Equal(r.Errors.Count, back.Errors.Count);
            var j = back.Root.Child("j")!;
            Assert.Equal("junction", j.ReparseTag);
            Assert.Equal(fx.LongFile, back.Root.Find(fx.LongFile)!.FullPath);
            Assert.Single(index.List());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*"))
                File.Delete(f);
        }
    }

    [Fact]
    public async Task TreeCodecRoundTrip()
    {
        var r = await new FileScanner().ScanAsync(fx.Root, new ScanOptions(), null, default);
        var source = new ScanResult
        {
            Root = r.Root,
            Files = r.Files,
            Directories = r.Directories,
            Elapsed = r.Elapsed,
            FinishedAt = r.FinishedAt,
            Errors = [.. r.Errors, "ikinci hata"],
            Cancelled = true,
            Method = "MFT",
            Usn = new UsnCursor(0xFEDCBA9876543210, 123456789),
        };
        var file = r.Root.Descendants().First(n => !n.IsDirectory);
        file.CloudSize = 777;
        file.NewestWriteTicks = file.LastWriteTicks + 5;
        file.Flags |= NodeFlags.CloudPlaceholder;
        using var stream = new MemoryStream();
        ScanTreeCodec.Write(source, stream);
        stream.Position = 0;
        var back = ScanTreeCodec.Read(stream);
        output.WriteLine($"{r.Files + r.Directories + 1} düğüm, {stream.Length} bayt");

        Assert.Equal(source.Files, back.Files);
        Assert.Equal(source.Directories, back.Directories);
        Assert.Equal(source.Elapsed, back.Elapsed);
        Assert.Equal(source.FinishedAt, back.FinishedAt);
        Assert.Equal(source.Errors, back.Errors);
        Assert.True(back.Cancelled);
        Assert.Equal("MFT", back.Method);
        Assert.Equal(source.Usn, back.Usn);
        Assert.Null(back.Root.Parent);
        Assert.Equal(Flatten(source.Root), Flatten(back.Root));
        Assert.Equal(fx.LongFile, back.Root.Find(fx.LongFile)!.FullPath);
        Assert.All(back.Root.Descendants(), n => Assert.Contains(n, n.Parent!.Children!));

        static List<string> Flatten(ScanNode root) =>
            [.. new[] { root }.Concat(root.Descendants()).Select(n =>
                $"{n.FullPath}|{n.IsDirectory}|{n.Size}|{n.LogicalSize}|{n.CloudSize}|{n.LastWriteTicks}|{n.NewestWriteTicks}|{n.FileCount}|{n.Flags}|{n.ReparseTag}|{n.Children?.Count ?? 0}")];
    }

    [Fact]
    [Trait("Kind", "Machine")]
    public async Task FastScannerMatchesFileScanner()
    {
        if (!ScanFixture.IsAdmin)
            return;
        var dir = new[] { @"C:\Program Files\dotnet", @"C:\Windows\Fonts" }.First(Directory.Exists);
        var fast = await new FastScanner().ScanAsync(dir, new ScanOptions(), null, default);
        var slow = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
        output.WriteLine($"{dir}: MFT {fast.Files} dosya {fast.Root.Size} B {fast.Elapsed.TotalMilliseconds:F0} ms; FindFirstFileEx {slow.Files} dosya {slow.Root.Size} B {slow.Elapsed.TotalMilliseconds:F0} ms");
        output.WriteLine($"FileCount MFT {fast.Root.FileCount} / FFF {slow.Root.FileCount}; mantıksal MFT {fast.Root.LogicalSize} / FFF {slow.Root.LogicalSize}");
        foreach (var e in fast.Errors)
            output.WriteLine("MFT: " + e);
        Assert.Equal("MFT", fast.Method);
        Assert.Equal(slow.Files, fast.Files);
        Assert.Equal(slow.Directories, fast.Directories);
        Assert.Equal(slow.Root.LogicalSize, fast.Root.LogicalSize);
        var ratio = (double)fast.Root.Size / slow.Root.Size;
        Assert.InRange(ratio, 0.95, 1.01);
    }

    [Fact]
    public async Task UsnIncrementalUpdate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dustybytes-usn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "silinecek.bin"), new byte[4000]);
        File.WriteAllBytes(Path.Combine(dir, "buyuyecek.bin"), new byte[100]);
        var r = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
        if (r.Usn is null)
        {
            Directory.Delete(dir, true);
            return;
        }
        Assert.Equal(2, r.Root.FileCount);

        File.Delete(Path.Combine(dir, "silinecek.bin"));
        File.WriteAllBytes(Path.Combine(dir, "buyuyecek.bin"), new byte[20000]);
        File.WriteAllBytes(Path.Combine(dir, "yeni.bin"), new byte[7000]);
        Directory.CreateDirectory(Path.Combine(dir, "alt"));
        File.WriteAllBytes(Path.Combine(dir, "alt", "ic.bin"), new byte[500]);

        var changes = UsnJournal.ReadSince(dir, r.Usn, out var next);
        Assert.NotEmpty(changes);
        Assert.True(next.NextUsn > r.Usn.NextUsn);
        if (ScanFixture.IsAdmin)
            Assert.Contains(UsnJournal.ReadSince(dir, r.Usn, out _, privileged: true), c => c.Name == "yeni.bin");

        var u = await UsnUpdater.ApplyAsync(r);
        var root = u.Result.Root;
        output.WriteLine($"Değişiklik {u.Changes}, eklenen {u.Added}, silinen {u.Removed}, güncellenen {u.Updated}, çözülemeyen {u.Unresolved}");
        Assert.Null(root.Child("silinecek.bin"));
        Assert.Equal(20000, root.Child("buyuyecek.bin")!.LogicalSize);
        Assert.Equal(7000, root.Child("yeni.bin")!.LogicalSize);
        Assert.Equal(500, root.Child("alt")!.Child("ic.bin")!.LogicalSize);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(20000 + 7000 + 500, root.LogicalSize);

        var fresh = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
        Assert.Equal(fresh.Root.Size, root.Size);
        Assert.Equal(fresh.Files, u.Result.Files);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task UsnRefreshMatchesFullScan()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dustybytes-usn-" + Guid.NewGuid().ToString("N")[..8]);
        void Put(string rel, int size)
        {
            var p = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[size]);
        }
        Put("silinecek.bin", 4000);
        Put("buyuyecek.bin", 100);
        Put("adi-degisecek.bin", 300);
        Put(@"klasor\ic1.bin", 1000);
        Put(@"klasor\alt\ic2.bin", 2000);
        Put(@"tasinacak\t.bin", 5000);
        Put(@"hedef\duran.bin", 10);
        Put(@"agac\a\b\c.bin", 9000);
        Put(@"agac\d.bin", 700);
        Put(@"derin\x\y\z\eski.bin", 64);
        Put(@"derin\x\y\z\degisen.bin", 64);
        Directory.CreateDirectory(Path.Combine(dir, "bos"));
        try
        {
            var r = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
            if (r.Usn is null)
                return;

            File.Delete(Path.Combine(dir, "silinecek.bin"));
            Put("buyuyecek.bin", 50000);
            File.Move(Path.Combine(dir, "adi-degisecek.bin"), Path.Combine(dir, "yeni-ad.bin"));
            Directory.Move(Path.Combine(dir, "klasor"), Path.Combine(dir, "klasor2"));
            Directory.Move(Path.Combine(dir, "tasinacak"), Path.Combine(dir, "hedef", "tasinan"));
            Directory.Delete(Path.Combine(dir, "agac"), true);
            Put(@"yeni\n1\n2\f.bin", 12345);
            Put(@"derin\x\y\z\yeni.bin", 777);
            Put(@"derin\x\y\z\degisen.bin", 8888);
            Put(@"bos\artik-dolu.bin", 1);

            var u = await UsnUpdater.ApplyAsync(r);
            output.WriteLine($"Değişiklik {u.Changes}, eklenen {u.Added}, silinen {u.Removed}, güncellenen {u.Updated}, çözülemeyen {u.Unresolved}");
            var fresh = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
            Assert.Equal(Flatten(fresh.Root), Flatten(u.Result.Root));
            Assert.Equal(fresh.Files, u.Result.Files);
            Assert.Equal(fresh.Directories, u.Result.Directories);
            Assert.True(u.Result.Usn!.NextUsn > r.Usn.NextUsn);
        }
        finally
        {
            Directory.Delete(dir, true);
        }

        static List<string> Flatten(ScanNode root) =>
            [.. new[] { root }.Concat(root.Descendants())
                .Select(n => $"{Path.GetRelativePath(root.FullPath, n.FullPath)}|{n.IsDirectory}|{n.Size}|{n.LogicalSize}|{(n.IsDirectory ? 0 : n.LastWriteTicks)}|{n.NewestWriteTicks}|{n.FileCount}|{n.Flags}|{n.ReparseTag}")
                .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
