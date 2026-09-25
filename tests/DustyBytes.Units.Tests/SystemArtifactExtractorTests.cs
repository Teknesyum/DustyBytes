using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class SystemArtifactExtractorTests
{
    [Fact]
    public void FindsWindowsOldAndSoftwareDistribution()
    {
        var root = Tree.Dir(@"C:\",
            Tree.Dir("Windows.old", Tree.File("big.bin", 20_000_000_000)),
            Tree.Dir("Windows", Tree.Dir("SoftwareDistribution", Tree.Dir("Download", Tree.File("update.cab", 500_000_000)))));

        var units = new SystemArtifactExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Equal(2, units.Count);
        Assert.All(units, u => Assert.Equal(UnitKind.SystemArtifact, u.Kind));
        Assert.All(units, u => Assert.Equal(RemovalMethod.SystemTool, u.Removal));
    }

    [Fact]
    public void MissingTargetsYieldNoUnits()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Windows"));

        var units = new SystemArtifactExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
