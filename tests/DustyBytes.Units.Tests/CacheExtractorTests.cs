using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class CacheExtractorTests
{
    [Fact]
    public void FindsUserTempAndSystemTemp()
    {
        var root = Tree.Dir(@"C:\",
            Tree.Dir("Windows", Tree.Dir("Temp", Tree.File("a.tmp", 1_000_000))),
            Tree.Dir("Users", Tree.Dir("alice", Tree.Dir("AppData", Tree.Dir("Local",
                Tree.Dir("Temp", Tree.File("b.tmp", 2_000_000)))))));

        var units = new CacheExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Equal(2, units.Count);
        Assert.All(units, u => Assert.Equal(UnitKind.Cache, u.Kind));
        Assert.All(units, u => Assert.Equal(RemovalMethod.DirectDelete, u.Removal));
    }

    [Fact]
    public void ThumbcacheOnlyIncludesThumbAndIconCacheFiles()
    {
        var explorer = Tree.Dir("Explorer",
            Tree.File("thumbcache_256.db", 5_000_000),
            Tree.File("iconcache_16.db", 1_000_000),
            Tree.File("unrelated.ini", 100));
        var root = Tree.Dir(@"C:\",
            Tree.Dir("Users", Tree.Dir("alice", Tree.Dir("AppData", Tree.Dir("Local",
                Tree.Dir("Microsoft", Tree.Dir("Windows", explorer)))))));

        var unit = new CacheExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(6_000_000, unit.SizeBytes);
        Assert.DoesNotContain(unit.Paths, p => p.EndsWith("unrelated.ini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyTempFolderYieldsNoUnit()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Windows", Tree.Dir("Temp")));

        var units = new CacheExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
