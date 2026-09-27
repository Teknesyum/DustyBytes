using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;
using DustyBytes.Units;

namespace DustyBytes.Tests;

public sealed class OyunKarantinaProvasi
{
    const string Variable = "DUSTYBYTES_OYUN_PROVA";

    [Fact]
    public void Oyunlar_Tek_Tikla_Karantinaya_Gider()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } output)
            return;
        Directory.CreateDirectory(output);
        var saved = Environment.GetEnvironmentVariable(DryRun.Variable);
        Environment.SetEnvironmentVariable(DryRun.Variable, "1");
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory)!;
            var result = new ScanIndex().Load(root)!;
            var usage = UsageIndex.Collect();
            var protection = ProtectedList.LoadDefault();
            foreach (var r in usage.LibraryRoots)
                protection.AddLauncherLibrary(r, "Oyun");
            var units = UnitBuilder.Build(new UnitContext
            {
                ScanResult = result,
                UsageIndex = usage,
                Protected = protection,
                Now = DateTimeOffset.Now,
            });
            var store = new QuarantineStore(SafetyGate.LoadDefault());
            var lines = new List<string>();
            foreach (var game in usage.Games)
            {
                var unit = units.FirstOrDefault(u => u.Kind == UnitKind.Game && u.Name == game.Name);
                if (unit is null)
                {
                    lines.Add($"{game.Name}\tÖNERİLMEDİ\t{game.InstallDir}");
                    continue;
                }
                lines.Add($"{unit.Name}\t{Format.Bytes(unit.SizeBytes)}\t{unit.Removal}");
                foreach (var path in unit.Paths)
                {
                    var op = store.Quarantine(path, unit.Id, unit.ContainsUserData);
                    lines.Add($"  {op.Status}\t{path}\t{op.Message}");
                }
            }
            File.WriteAllLines(Path.Combine(output, "oyun-prova.txt"), lines);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DryRun.Variable, saved);
        }
    }
}
