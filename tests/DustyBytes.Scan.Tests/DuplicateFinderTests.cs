using System.Diagnostics;
using DustyBytes.Scan.Duplicates;

namespace DustyBytes.Scan.Tests;

public sealed class DuplicateFinderTests : IDisposable
{
    const long Min = 16 * 1024;
    readonly string _root = Path.Combine(Path.GetTempPath(), "DustyBytes.Duplicates", Guid.NewGuid().ToString("N"));

    public DuplicateFinderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    static byte[] Bytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    string Put(string relative, byte[] data)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    async Task<List<DuplicateCandidate>> Candidates(long min = Min)
    {
        var scan = await new FileScanner().ScanAsync(_root, new ScanOptions(), null, CancellationToken.None);
        return DuplicateFinder.Candidates([scan.Root], min);
    }

    [Fact]
    public async Task Ayni_Icerik_Gruplanir_Farkli_Icerik_Ayrilir()
    {
        var film = Bytes(200_000, 1);
        var a = Put(@"belgeler\film.mkv", film);
        var b = Put(@"yedek\film.mkv", film);
        var c = Put(@"indirilenler\film (1).mkv", film);
        var other = Bytes(200_000, 2);
        Put(@"baska\ayni-boy.mkv", other);
        var middle = (byte[])film.Clone();
        middle[100_000] ^= 0xFF;
        Put(@"baska\ortasi-farkli.mkv", middle);
        Put(@"kucuk\a.bin", Bytes(4_000, 3));
        Put(@"kucuk\b.bin", Bytes(4_000, 3));

        var finder = new DuplicateFinder();
        var streamed = new List<DuplicateGroup>();
        var groups = finder.Find(await Candidates(), streamed.Add, null, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Single(streamed);
        Assert.Equal(200_000, group.Length);
        Assert.Equal(new[] { a, b, c }.Order(StringComparer.OrdinalIgnoreCase), group.Copies.Select(x => x.Path).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        Assert.True(group.Reclaimable > 0);
    }

    [Fact]
    public async Task Bas_Son_Ozeti_Farkliysa_Tam_Ozet_Okunmaz()
    {
        Put(@"a\bir.bin", Bytes(100_000, 1));
        Put(@"b\iki.bin", Bytes(100_000, 2));
        Put(@"c\uc.bin", Bytes(100_000, 3));

        var finder = new DuplicateFinder();
        var groups = finder.Find(await Candidates(), null, null, CancellationToken.None);

        Assert.Empty(groups);
        Assert.Equal(0, finder.FullHashes);
        Assert.Equal(3, finder.Opened);
    }

    [Fact]
    public async Task Tek_Boyutlu_Dosya_Hic_Acilmaz()
    {
        Put(@"a\bir.bin", Bytes(100_000, 1));
        Put(@"b\iki.bin", Bytes(100_001, 1));

        var finder = new DuplicateFinder();
        var groups = finder.Find(await Candidates(), null, null, CancellationToken.None);

        Assert.Empty(groups);
        Assert.Equal(0, finder.Opened);
    }

    [Fact]
    public async Task Sabit_Baglanti_Kopya_Sayilmaz()
    {
        var data = Bytes(100_000, 5);
        var original = Put(@"a\asil.bin", data);
        var link = Path.Combine(_root, @"b\baglanti.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        HardLink(link, original);

        var facts = FileProbe.Probe(original);
        Assert.Equal(FileState.Ready, facts.State);
        Assert.Equal(2u, facts.Links);
        Assert.Equal(facts.Key, FileProbe.Probe(link).Key);

        var finder = new DuplicateFinder();
        var direct = finder.Find([new DuplicateCandidate(original, data.Length, data.Length), new DuplicateCandidate(link, data.Length, data.Length)], null, null, CancellationToken.None);
        Assert.Empty(direct);

        Put(@"c\gercek-kopya.bin", data);
        var mixed = finder.Find(await Candidates(), null, null, CancellationToken.None);
        Assert.Empty(mixed);
    }

    [Fact]
    public void Bulut_Yer_Tutucusu_Aday_Olmaz_Ve_Acilmaz()
    {
        var root = new ScanNode { Name = _root, IsDirectory = true, Children = [] };
        root.Children.Add(new ScanNode { Name = "bulut.mkv", Parent = root, LogicalSize = Min * 4, Size = 0, Flags = NodeFlags.CloudPlaceholder });
        root.Children.Add(new ScanNode { Name = "yerel.mkv", Parent = root, LogicalSize = Min * 4, Size = Min * 4 });

        var candidates = DuplicateFinder.Candidates([root], Min);

        Assert.Equal("yerel.mkv", Path.GetFileName(Assert.Single(candidates).Path));
        Assert.True(FileProbe.IsCloud(FileAttributes.Offline));
        Assert.True(FileProbe.IsCloud((FileAttributes)0x00400000));
        Assert.False(FileProbe.IsCloud(FileAttributes.Archive));
    }

    [Fact]
    public async Task Klasor_Atlama_Ve_Esik_Uygulanir()
    {
        var data = Bytes(100_000, 7);
        Put(@"a\bir.bin", data);
        Put(@"atla\iki.bin", data);

        var skipped = await new FileScanner().ScanAsync(_root, new ScanOptions(), null, CancellationToken.None);
        var some = DuplicateFinder.Candidates([skipped.Root], Min, p => Path.GetFileName(p) == "atla");
        var none = await Candidates(200_000);

        Assert.Single(some);
        Assert.Empty(none);
    }

    [Fact]
    public async Task Iptal_Edilince_Durur()
    {
        var data = Bytes(100_000, 9);
        Put(@"a\bir.bin", data);
        Put(@"b\iki.bin", data);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var candidates = await Candidates();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DuplicateFinder().FindAsync(candidates, null, null, cts.Token));
    }

    [Fact]
    public async Task Onbellek_Degismeyen_Dosyayi_Yeniden_Okumaz()
    {
        var data = Bytes(100_000, 11);
        Put(@"a\bir.bin", data);
        Put(@"b\iki.bin", data);
        var file = Path.Combine(_root, "hash.json");
        var candidates = await Candidates();

        var first = new HashCache(file);
        var cold = new DuplicateFinder(first);
        Assert.Single(cold.Find(candidates, null, null, CancellationToken.None));
        first.Save();
        Assert.Equal(2, cold.FullHashes);

        var warm = new DuplicateFinder(new HashCache(file));
        var progress = new List<DuplicateProgress>();
        Assert.Single(warm.Find(candidates, null, new Sync<DuplicateProgress>(progress.Add), CancellationToken.None));
        Assert.Equal(0, warm.FullHashes);
        Assert.Equal(100, progress[^1].Percent);
    }

    sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    static void HardLink(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (!File.Exists(link))
            throw new InvalidOperationException("Sabit bağlantı oluşturulamadı: " + p.StandardError.ReadToEnd());
    }
}
