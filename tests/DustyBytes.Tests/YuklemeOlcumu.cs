using System.Diagnostics;
using DustyBytes.App.Services;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;
using DustyBytes.Units;

namespace DustyBytes.Tests;

public sealed class YuklemeOlcumu
{
    const string Variable = "DUSTYBYTES_OLCUM";

    [Fact]
    public void Acilis_Asamalari()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } output)
            return;
        Directory.CreateDirectory(output);
        var lines = new List<string>();
        var total = Stopwatch.StartNew();
        T Time<T>(string name, Func<T> work)
        {
            var watch = Stopwatch.StartNew();
            var value = work();
            lines.Add($"{name}\t{watch.ElapsedMilliseconds} ms");
            return value;
        }

        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var result = Time("ScanIndex.Load", () => new ScanIndex().Load(root))!;
        lines.Add($"  dosya {result.Files}, klasör {result.Directories}");
        var usage = Time("UsageIndex.Collect", () => UsageIndex.Collect());
        lines.Add($"  oyun {usage.Games.Count}, kütüphane {usage.LibraryRoots.Count}");
        var protection = Time("Protection", () =>
        {
            var list = ProtectedList.LoadDefault();
            foreach (var r in usage.LibraryRoots)
                list.AddLauncherLibrary(r, "Oyun");
            return list;
        });
        var programs = Time("InstalledPrograms.Enumerate", () => InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = false }));
        var forUnits = Time("UnitPrograms.From", () => UnitPrograms.From(programs, protection));
        var ctx = new UnitContext
        {
            ScanResult = result,
            UsageIndex = usage,
            Protected = protection,
            Now = DateTimeOffset.Now,
            Programs = forUnits,
        };
        var units = Time("UnitBuilder.Build", () => UnitBuilder.Build(ctx));
        lines.Add($"  birim {units.Count}");
        File.WriteAllLines(Path.Combine(output, "birim-kimlikleri.txt"), units.Select(u => $"{u.Kind} {u.Id} {u.SizeBytes}").Order(StringComparer.Ordinal));
        lines.Add($"Toplam\t{total.ElapsedMilliseconds} ms");
        File.WriteAllLines(Path.Combine(output, "yukleme.txt"), lines);
    }
}
