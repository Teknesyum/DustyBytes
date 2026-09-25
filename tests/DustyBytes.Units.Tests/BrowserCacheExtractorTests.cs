using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class BrowserCacheExtractorTests
{
    [Fact]
    public void ChromeProfileYieldsOneUnitPerBrowserNeverIncludingHistoryOrCookies()
    {
        var profile = Tree.Dir("Default",
            Tree.Dir("Cache", Tree.File("data_1", 10_000_000)),
            Tree.Dir("Code Cache", Tree.File("index", 1_000_000)),
            Tree.Dir("GPUCache", Tree.File("data_0", 500_000)),
            Tree.Dir("Service Worker", Tree.Dir("CacheStorage", Tree.File("blob", 200_000))),
            Tree.File("History", 400_000),
            Tree.File("Cookies", 100_000));
        var userData = Tree.Dir("User Data", profile);
        var root = Tree.Dir(@"C:\",
            Tree.Dir("Users", Tree.Dir("alice", Tree.Dir("AppData", Tree.Dir("Local",
                Tree.Dir("Google", Tree.Dir("Chrome", userData)))))));

        var units = new BrowserCacheExtractor().Extract(Ctx.Build(root)).ToList();

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.BrowserCache, unit.Kind);
        Assert.Equal(11_700_000, unit.SizeBytes);
        Assert.DoesNotContain(unit.Paths, p => p.Contains("History", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(unit.Paths, p => p.Contains("Cookies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MultipleProfilesAggregateIntoOneBrowserUnit()
    {
        var profile1 = Tree.Dir("Default", Tree.Dir("Cache", Tree.File("d1", 1_000_000)));
        var profile2 = Tree.Dir("Profile 1", Tree.Dir("Cache", Tree.File("d2", 2_000_000)));
        var userData = Tree.Dir("User Data", profile1, profile2);
        var root = Tree.Dir(@"C:\",
            Tree.Dir("Users", Tree.Dir("bob", Tree.Dir("AppData", Tree.Dir("Local",
                Tree.Dir("Microsoft", Tree.Dir("Edge", userData)))))));

        var unit = new BrowserCacheExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(3_000_000, unit.SizeBytes);
        Assert.Equal(2, unit.Paths.Count);
    }

    [Fact]
    public void NoBrowserFoldersYieldsNoUnits()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice")));

        var units = new BrowserCacheExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
