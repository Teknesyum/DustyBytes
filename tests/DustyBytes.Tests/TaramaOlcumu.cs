using System.Diagnostics;
using DustyBytes.App.Services;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;
using DustyBytes.Units;
using DustyBytes.Worker;

namespace DustyBytes.Tests;

public sealed class TaramaOlcumu
{
    const string Variable = "DUSTYBYTES_TARAMA_OLCUM";

    static string? Output => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } o ? o : null;

    sealed class Sink : IProgress<TaskStep>
    {
        public void Report(TaskStep value)
        {
        }
    }

    sealed class Drafts(Action<ScanDraft> report) : IProgress<ScanDraft>
    {
        public void Report(ScanDraft value) => report(value);
    }

    [Fact]
    public async Task Akista_Ilk_Kart()
    {
        if (Output is not { } output)
            return;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DUSTYBYTES_TARAMA_TUR"), out var n) ? n : 3;
        var lines = new List<string> { $"tarih={DateTimeOffset.Now:O}" };
        for (var i = 0; i < runs; i++)
        {
            await using var backend = new AppBackend();
            var watch = Stopwatch.StartNew();
            long first = -1, firstSettled = -1;
            var count = 0;
            var settled = new Dictionary<string, (long Size, long At)>(StringComparer.Ordinal);
            var gate = new object();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var sink = new Drafts(d =>
            {
                lock (gate)
                {
                    count++;
                    if (first < 0 && d.Units.Count > 0)
                        first = watch.ElapsedMilliseconds;
                    foreach (var u in d.Units.Where(u => !d.Pending.Contains(u.Id)))
                    {
                        if (firstSettled < 0)
                            firstSettled = watch.ElapsedMilliseconds;
                        settled.TryAdd(u.Id, (u.SizeBytes, watch.ElapsedMilliseconds));
                        names.TryAdd(u.Id, $"{u.Kind} {u.Name} {u.Paths[0]}");
                    }
                }
            });
            var snapshot = await backend.ScanAsync(new Sink(), default, sink);
            var total = watch.ElapsedMilliseconds;
            var final = Map(snapshot.Units);
            var missing = settled.Keys.Where(k => !final.ContainsKey(k)).ToList();
            var differ = settled.Where(p => final.TryGetValue(p.Key, out var f) && f.SizeBytes != p.Value.Size).ToList();
            lines.Add($"tur {i + 1}: ilk_kart_ms={first} ilk_kesin_kart_ms={firstSettled} taslak={count} kesin_kart={settled.Count} toplam_ms={total} birim={snapshot.Units.Count} sonda_yok={missing.Count} boyut_farkı={differ.Count}");
            foreach (var k in missing)
                lines.Add($"  sonda yok: {k} {names[k]} {settled[k].Size} @{settled[k].At} ms");
            foreach (var p in differ)
                lines.Add($"  boyut farkı: {final[p.Key].Kind} {p.Key} {p.Value.Size} -> {final[p.Key].SizeBytes}");
            foreach (var p in settled.OrderBy(p => p.Value.At).Take(10))
                lines.Add($"  {p.Value.At} ms {final.GetValueOrDefault(p.Key)?.Kind} {final.GetValueOrDefault(p.Key)?.Name} {p.Value.Size}");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        Write(output, "akis.txt", lines);
    }

    static void Write(string output, string name, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(output);
        File.AppendAllLines(Path.Combine(output, name), lines);
    }

    static IReadOnlyList<Unit> Units(ScanResult result)
    {
        var usage = UsageIndex.Collect();
        var protection = ProtectedList.LoadDefault();
        foreach (var r in usage.LibraryRoots)
            protection.AddLauncherLibrary(r, "Oyun");
        var programs = UnitPrograms.From(InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = false }), protection);
        return UnitBuilder.Build(new UnitContext { ScanResult = result, UsageIndex = usage, Protected = protection, Now = DateTimeOffset.Now, Programs = programs });
    }

    static Dictionary<string, Unit> Map(IReadOnlyList<Unit> units) =>
        units.GroupBy(u => u.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    static IEnumerable<string> Diff(string label, IReadOnlyList<Unit> a, IReadOnlyList<Unit> b)
    {
        var left = Map(a);
        var right = Map(b);
        var onlyA = left.Keys.Except(right.Keys).Order(StringComparer.Ordinal).ToList();
        var onlyB = right.Keys.Except(left.Keys).Order(StringComparer.Ordinal).ToList();
        var sized = left.Keys.Intersect(right.Keys).Where(k => left[k].SizeBytes != right[k].SizeBytes).Order(StringComparer.Ordinal).ToList();
        yield return $"{label}: birim {a.Count} / {b.Count}, toplam {a.Sum(u => u.SizeBytes)} / {b.Sum(u => u.SizeBytes)} bayt, yalnız solda {onlyA.Count}, yalnız sağda {onlyB.Count}, boyutu farklı {sized.Count}";
        foreach (var k in onlyA)
            yield return $"  - {left[k].Kind} {k} {left[k].SizeBytes}";
        foreach (var k in onlyB)
            yield return $"  + {right[k].Kind} {k} {right[k].SizeBytes}";
        foreach (var k in sized)
            yield return $"  ~ {left[k].Kind} {k} {left[k].Paths[0]} {left[k].SizeBytes} -> {right[k].SizeBytes}";
    }

    [Fact]
    public async Task Normal_Ve_Yenileme()
    {
        if (Output is not { } output)
            return;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DUSTYBYTES_TARAMA_TUR"), out var n) ? n : 3;
        await using var backend = new AppBackend();
        var sink = new Sink();
        var lines = new List<string> { $"tarih={DateTimeOffset.Now:O} kök={backend.ScanRoot}" };
        var watch = Stopwatch.StartNew();
        var previous = await backend.ScanAsync(sink, default);
        lines.Add($"normal_ms={watch.ElapsedMilliseconds} dosya={previous.Result.Files} klasör={previous.Result.Directories} birim={previous.Units.Count} usn={(previous.Result.Usn is null ? "yok" : "var")}");
        for (var i = 0; i < runs; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            watch.Restart();
            var refreshed = await backend.RefreshAsync(previous.Result, sink, default);
            var refreshMs = watch.ElapsedMilliseconds;
            lines.Add(refreshed is null ? $"yenileme_ms={refreshMs} sonuç=yok" : $"yenileme_ms={refreshMs} dosya={refreshed.Result.Files} birim={refreshed.Units.Count}");
            await Task.Delay(TimeSpan.FromSeconds(15));
            watch.Restart();
            var full = await backend.ScanAsync(sink, default);
            lines.Add($"normal_ms={watch.ElapsedMilliseconds} dosya={full.Result.Files} klasör={full.Result.Directories} birim={full.Units.Count}");
            if (refreshed is not null)
            {
                var diff = Diff($"tur {i + 1} yenileme / hemen ardından tam tarama", refreshed.Units, full.Units).ToList();
                lines.Add(diff[0]);
                Write(output, "yenileme-farki.txt", diff);
                watch.Restart();
                var again = await backend.RefreshAsync(previous.Result, sink, default);
                lines.Add($"ikinci_yenileme_ms={watch.ElapsedMilliseconds}");
                if (again is not null)
                {
                    var check = Diff($"tur {i + 1} aynı başlangıçtan ikinci yenileme / tam tarama", again.Units, full.Units).ToList();
                    lines.Add(check[0]);
                    Write(output, "yenileme-farki.txt", check);
                }
            }
            previous = full;
        }
        Write(output, "normal-ve-yenileme.txt", lines);
    }

    [Fact]
    public async Task Hizli_Tarama_Once_Sonra()
    {
        if (Output is not { } output || !FastScanner.IsSupported(@"C:\"))
            return;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DUSTYBYTES_TARAMA_TUR"), out var n) ? n : 3;
        var lines = new List<string> { $"tarih={DateTimeOffset.Now:O} süreç yönetici={Environment.IsPrivilegedProcess}" };
        for (var i = 0; i < runs; i++)
        {
            var total = Stopwatch.StartNew();
            var response = await WorkerBindings.FastScan(new WorkerRequest { Op = Ops.FastScan, Target = @"C:\" }, new Progress<WorkerProgress>(), default);
            var worker = total.ElapsedMilliseconds;
            Assert.True(response.Ok, response.Message);
            var watch = Stopwatch.StartNew();
            var result = ScanTreeCodec.Read(response.Payload!);
            var read = watch.ElapsedMilliseconds;
            var size = new FileInfo(response.Payload!).Length;
            File.Delete(response.Payload!);
            watch.Restart();
            var units = Units(result);
            var build = watch.ElapsedMilliseconds;
            var after = total.ElapsedMilliseconds;

            var db = Path.Combine(Path.GetTempPath(), $"dustybytes-olcum-{Guid.NewGuid():N}.db");
            long save, load;
            try
            {
                var index = new ScanIndex(db);
                watch.Restart();
                index.Save(result);
                save = watch.ElapsedMilliseconds;
                watch.Restart();
                index.Load(@"C:\");
                load = watch.ElapsedMilliseconds;
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*"))
                    File.Delete(f);
            }
            lines.Add($"tur {i + 1}: worker(mft+ikili yazma)_ms={worker} ikili_okuma_ms={read} ikili_bayt={size} birim_ms={build} birim={units.Count} sonra_toplam_ms={after} | sqlite_kaydet_ms={save} sqlite_yükle_ms={load} önce_toplam_ms={worker + save + load + build}");
        }
        Write(output, "hizli-tarama.txt", lines);
    }
}
