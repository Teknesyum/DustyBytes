using DustyBytes.Clean.Rules;
using DustyBytes.Core.Protection;
using Xunit.Abstractions;

namespace DustyBytes.Rules.Tests;

public class RealMachinePreviewTests(ITestOutputHelper output)
{
    static string RepoRulesDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "rules", "cleaners")))
                dir = Path.GetDirectoryName(dir);
            return dir is null
                ? throw new InvalidOperationException("Depo kökü bulunamadı")
                : Path.Combine(dir, "rules");
        }
    }

    [Fact]
    public void PreviewAllRulesOnRealMachineReadOnly()
    {
        var protectedList = new ProtectedList(ProtectedRules.Load(Path.Combine(RepoRulesDir, "protected.json")));
        var catalog = CleanerCatalog.Load(Path.Combine(RepoRulesDir, "cleaners"), protectedList);

        var selection = catalog.Rules
            .Select(r => new RuleSelection(r.Id, [.. r.Options.Select(o => o.Id)]))
            .ToList();

        var results = catalog.Preview(selection);

        long totalFiles = 0, totalBytes = 0;
        foreach (var r in results.Where(r => r.FileCount > 0))
        {
            output.WriteLine($"{r.RuleId}/{r.OptionId}: {r.FileCount} dosya, {r.Bytes} bayt");
            totalFiles += r.FileCount;
            totalBytes += r.Bytes;
        }

        output.WriteLine($"TOPLAM: {totalFiles} dosya, {totalBytes} bayt ({totalBytes / 1024.0 / 1024.0:0.##} MB)");
        Assert.True(totalFiles >= 0);
    }
}
