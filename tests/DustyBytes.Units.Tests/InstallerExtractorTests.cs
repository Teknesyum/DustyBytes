using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class InstallerExtractorTests
{
    [Fact]
    public void InstallerWithExtractedSiblingFolderGetsHigherConfidence()
    {
        var downloads = Tree.Dir("Downloads",
            Tree.File("SomeTool-1.2.3.zip", 50_000_000),
            Tree.Dir("SomeTool-1.2.3", Tree.File("readme.txt", 100)),
            Tree.File("installer.exe", 20_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice", downloads)));

        var units = new InstallerExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Equal(2, units.Count);
        var withSibling = units.Single(u => u.Name == "SomeTool-1.2.3.zip");
        var withoutSibling = units.Single(u => u.Name == "installer.exe");

        Assert.True(withSibling.Confidence > withoutSibling.Confidence);
        Assert.Contains("açılmış kopyası var", withSibling.Reason);
        Assert.All(units, u => Assert.Equal(RemovalMethod.Quarantine, u.Removal));
    }

    [Fact]
    public void NonInstallerExtensionsAreIgnored()
    {
        var downloads = Tree.Dir("Downloads", Tree.File("photo.jpg", 5_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice", downloads)));

        var units = new InstallerExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
