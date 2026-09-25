using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class DevArtifactExtractorTests
{
    static DevArtifactRules Rules() => DevArtifactRules.LoadDefault();

    [Fact]
    public void DotNetProjectYieldsSingleUnitWithBinAndObj()
    {
        var proj = Tree.Dir("MyApp",
            Tree.File("MyApp.csproj", 2_000, Ctx.Now.AddDays(-5)),
            Tree.File("Program.cs", 500, Ctx.Now.AddDays(-2)),
            Tree.Dir("bin", Tree.File("MyApp.dll", 300_000_000)),
            Tree.Dir("obj", Tree.File("MyApp.AssemblyInfo.cs", 1_000)));
        var root = Tree.Dir(@"C:\", Tree.Dir("Code", proj));

        var extractor = new DevArtifactExtractor(Rules());
        var units = extractor.Extract(Ctx.Build(root)).ToList();

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.DevArtifact, unit.Kind);
        Assert.Contains(".NET", unit.Name);
        Assert.Equal(300_001_000, unit.SizeBytes);
        Assert.Equal(RemovalMethod.DirectDelete, unit.Removal);
        Assert.Equal(2, unit.Paths.Count);
        Assert.Contains(unit.Paths, p => p.EndsWith(@"\bin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(unit.Paths, p => p.EndsWith(@"\obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NodeProjectDoesNotDescendIntoNodeModulesForNestedMarkers()
    {
        var nestedPackage = Tree.Dir("some-dep", Tree.File("package.json", 200));
        var nodeModules = Tree.Dir("node_modules", nestedPackage, Tree.File("stub.js", 100));
        var proj = Tree.Dir("web-app",
            Tree.File("package.json", 300, Ctx.Now.AddDays(-1)),
            nodeModules);
        var root = Tree.Dir(@"C:\", Tree.Dir("Code", proj));

        var units = new DevArtifactExtractor(Rules()).Extract(Ctx.Build(root)).ToList();

        Assert.Single(units);
        Assert.DoesNotContain(units, u => u.Paths.Any(p => p.Contains("some-dep", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ReasonReferencesProjectNameAndAge()
    {
        var proj = Tree.Dir("Tool",
            Tree.File("Cargo.toml", 100, Ctx.Now.AddDays(-40)),
            Tree.Dir("target", Tree.File("build.o", 50_000_000, Ctx.Now.AddDays(-40))));
        var root = Tree.Dir(@"C:\", proj);

        var unit = new DevArtifactExtractor(Rules()).Extract(Ctx.Build(root)).Single();

        Assert.Contains("Tool", unit.Reason);
        Assert.Contains("son değişiklik", unit.Reason);
    }

    [Fact]
    public void NoMarkerNoUnit()
    {
        var folder = Tree.Dir("Random", Tree.File("notes.txt", 100));
        var root = Tree.Dir(@"C:\", folder);

        var units = new DevArtifactExtractor(Rules()).Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }

    [Fact]
    public void ProjectWrittenInLastDayIsNotOffered()
    {
        var proj = Tree.Dir("Live",
            Tree.File("Live.csproj", 2_000, Ctx.Now.AddHours(-2)),
            Tree.Dir("bin", Tree.File("Live.dll", 300_000_000, Ctx.Now.AddHours(-1))));
        var root = Tree.Dir(@"C:\", Tree.Dir("Code", proj));

        var units = new DevArtifactExtractor(Rules()).Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
