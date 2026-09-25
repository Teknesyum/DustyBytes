using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace DustyBytes.Scan.Tests;

public sealed class BenchTests(ITestOutputHelper output)
{
    private static string? Target => Environment.GetEnvironmentVariable("DUSTYBYTES_BENCH");

    [Fact]
    public Task MeasureFileScanner() => Measure("file", new FileScanner());

    [Fact]
    public Task MeasureFastScanner() => ScanFixture.IsAdmin ? Measure("mft", new FastScanner()) : Task.CompletedTask;

    private async Task Measure(string kind, IScanner scanner)
    {
        if (Target is not { } root)
            return;
        var log = new StringBuilder();
        void Line(string s)
        {
            log.AppendLine(s);
            output.WriteLine(s);
        }

        var before = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var r = await scanner.ScanAsync(root, new ScanOptions(), null, default);
        sw.Stop();
        var retained = GC.GetTotalMemory(true) - before;
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        Line($"kök={root} tarayıcı={kind} yöntem={r.Method}");
        Line($"süre_ms={sw.ElapsedMilliseconds} dosya={r.Files} klasör={r.Directories} dosya_sayacı={r.Root.FileCount}");
        Line($"ayrılmış_bayt={r.Root.Size} mantıksal_bayt={r.Root.LogicalSize} bulut_bayt={r.Root.CloudSize}");
        Line($"ağaç_bellek_bayt={retained} tepe_çalışma_kümesi_bayt={proc.PeakWorkingSet64} hata={r.Errors.Count} iptal={r.Cancelled}");
        var dups = r.Root.Descendants().Count(n => (n.Flags & NodeFlags.HardLinkDuplicate) != 0);
        var reparse = r.Root.Descendants().Count(n => (n.Flags & NodeFlags.ReparsePoint) != 0);
        Line($"hardlink_tekrarı={dups} reparse={reparse}");
        foreach (var e in r.Errors.Take(5))
            Line("hata: " + e);

        var db = Path.Combine(Path.GetTempPath(), $"dustybytes-bench-{kind}-{Guid.NewGuid():N}.db");
        try
        {
            var index = new ScanIndex(db);
            sw.Restart();
            index.Save(r);
            var save = sw.ElapsedMilliseconds;
            sw.Restart();
            var back = index.Load(root)!;
            var load = sw.ElapsedMilliseconds;
            Line($"dizin_kaydet_ms={save} dizin_yükle_ms={load} db_bayt={new FileInfo(db).Length + (File.Exists(db + "-wal") ? new FileInfo(db + "-wal").Length : 0)} yüklenen_dosya={back.Files}");
            if (r.Usn is not null)
            {
                sw.Restart();
                var u = await UsnUpdater.ApplyAsync(back);
                Line($"usn_artımlı_ms={sw.ElapsedMilliseconds} değişiklik={u.Changes} eklenen={u.Added} silinen={u.Removed} güncellenen={u.Updated} çözülemeyen={u.Unresolved}");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*"))
                File.Delete(f);
        }
        GC.KeepAlive(r);

        var outDir = Environment.GetEnvironmentVariable("DUSTYBYTES_BENCH_OUT");
        if (outDir is not null)
            File.AppendAllText(Path.Combine(outDir, $"tarama-olcum.txt"), log + Environment.NewLine);
    }
}
