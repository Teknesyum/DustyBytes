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
            var back = index.Load(fx.Root)!;
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
        if (!ScanFixture.IsAdmin)
            return;
        var dir = Path.Combine(Path.GetTempPath(), "dustybytes-usn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "silinecek.bin"), new byte[4000]);
        File.WriteAllBytes(Path.Combine(dir, "buyuyecek.bin"), new byte[100]);
        var r = await new FileScanner().ScanAsync(dir, new ScanOptions(), null, default);
        Assert.NotNull(r.Usn);
        Assert.Equal(2, r.Root.FileCount);

        File.Delete(Path.Combine(dir, "silinecek.bin"));
        File.WriteAllBytes(Path.Combine(dir, "buyuyecek.bin"), new byte[20000]);
        File.WriteAllBytes(Path.Combine(dir, "yeni.bin"), new byte[7000]);
        Directory.CreateDirectory(Path.Combine(dir, "alt"));
        File.WriteAllBytes(Path.Combine(dir, "alt", "ic.bin"), new byte[500]);

        var changes = UsnJournal.ReadSince(dir, r.Usn!, out var next);
        Assert.Contains(changes, c => c.Name == "yeni.bin");
        Assert.True(next.NextUsn > r.Usn!.NextUsn);

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
}
