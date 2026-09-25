using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class LargeOldFolderExtractorTests
{
    [Fact]
    public void LargeOldFolderIsProposed()
    {
        var old = Tree.Dir("OldArchive", Tree.File("dump.bin", 2_000_000_000, Ctx.Now.AddDays(-400)));
        var root = Tree.Dir(@"C:\", old);

        var unit = new LargeOldFolderExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(UnitKind.Folder, unit.Kind);
        Assert.Equal(RemovalMethod.Quarantine, unit.Removal);
    }

    [Fact]
    public void SmallOrRecentFoldersAreSkipped()
    {
        var small = Tree.Dir("Small", Tree.File("f.bin", 10_000_000, Ctx.Now.AddDays(-400)));
        var recent = Tree.Dir("Recent", Tree.File("f.bin", 2_000_000_000, Ctx.Now.AddDays(-5)));
        var root = Tree.Dir(@"C:\", small, recent);

        var units = new LargeOldFolderExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }

    [Fact]
    public void OnlyDeepestQualifyingFolderIsProposedNotItsParent()
    {
        var deep = Tree.Dir("Deep", Tree.File("d.bin", 2_000_000_000, Ctx.Now.AddDays(-400)));
        var parent = Tree.Dir("Parent", deep, Tree.File("extra.bin", 2_000_000_000, Ctx.Now.AddDays(-400)));
        var root = Tree.Dir(@"C:\", parent);

        var units = new LargeOldFolderExtractor().Extract(Ctx.Build(root)).ToList();

        var unit = Assert.Single(units);
        Assert.Equal(deep.FullPath, unit.Paths.Single());
    }
}
