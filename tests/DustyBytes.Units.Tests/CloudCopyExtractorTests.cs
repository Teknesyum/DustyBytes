using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;

namespace DustyBytes.Units.Tests;

public sealed class CloudCopyExtractorTests
{
    const string SyncRoot = @"C:\Users\ali\OneDrive";

    static ScanNode Cloud(string name, long size, int ageDays, NodeFlags extra = NodeFlags.None)
    {
        var node = Tree.File(name, size, Ctx.Now.AddDays(-ageDays));
        node.ReparseTag = "cloud";
        node.Flags = NodeFlags.ReparsePoint | extra;
        return node;
    }

    static ProtectedList Protection()
    {
        var list = new ProtectedList(new ProtectedRules());
        list.AddRoot(SyncRoot, "Bulut, senkron: OneDrive", Badge.Cloud);
        list.AddSyncRoot(SyncRoot);
        return list;
    }

    static ScanNode Root(params ScanNode[] inside) =>
        Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("ali", Tree.Dir("OneDrive", inside))));

    [Fact]
    public void Old_Hydrated_Files_Are_Grouped_By_Top_Folder()
    {
        var root = Root(
            Tree.Dir("Arsiv",
                Cloud("eski.zip", 300_000_000, 400),
                Cloud("yeni.zip", 300_000_000, 10),
                Cloud("bulutta.zip", 300_000_000, 400, NodeFlags.CloudPlaceholder),
                Cloud("minik.txt", 1000, 400)),
            Tree.Dir("Yerel", Tree.File("yerel.bin", 500_000_000, Ctx.Now.AddDays(-400))));

        var unit = Assert.Single(new CloudCopyExtractor().Extract(Ctx.Build(root, protectedList: Protection())));

        Assert.Equal(UnitKind.CloudCopy, unit.Kind);
        Assert.Equal(RemovalMethod.CloudOnly, unit.Removal);
        Assert.True(unit.ContainsUserData);
        Assert.Equal("Arsiv", unit.Name);
        Assert.Equal([SyncRoot + @"\Arsiv\eski.zip"], unit.Paths);
        Assert.Equal(300_000_000, unit.SizeBytes);
    }

    [Fact]
    public void Probe_Filter_Drops_Files_Not_Fully_Synced()
    {
        var root = Root(Tree.Dir("Arsiv", Cloud("a.zip", 300_000_000, 400), Cloud("b.zip", 300_000_000, 400)));
        var ctx = Ctx.Build(root, protectedList: Protection()) with { CloudEligible = p => p.EndsWith("a.zip", StringComparison.Ordinal) };

        var unit = Assert.Single(new CloudCopyExtractor().Extract(ctx));

        Assert.Single(unit.Paths);
        Assert.EndsWith("a.zip", unit.Paths[0]);
        Assert.Empty(new CloudCopyExtractor().Extract(ctx with { CloudEligible = _ => false }));
    }

    [Fact]
    public void Nothing_Outside_A_Sync_Root()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Dropbox", Cloud("a.zip", 300_000_000, 400)));
        Assert.Empty(new CloudCopyExtractor().Extract(Ctx.Build(root, protectedList: Protection())));
    }

    [Fact]
    public void Builder_Keeps_Cloud_Units_Despite_Cloud_Protection()
    {
        var root = Root(Tree.Dir("Arsiv", Cloud("a.zip", 300_000_000, 400)));
        var units = UnitBuilder.Build(Ctx.Build(root, protectedList: Protection()), [new CloudCopyExtractor(), new LargeOldFolderExtractor()]);

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.CloudCopy, unit.Kind);
        Assert.False(UnitBuilder.CanCompress(unit));
    }

    [Fact]
    public void Builder_Counts_Already_Compressed_Bytes()
    {
        var packed = Tree.File("paket.bin", 1_500_000_000, Ctx.Now.AddDays(-400));
        packed.Flags = NodeFlags.Compressed;
        var root = Tree.Dir(@"D:\", Tree.Dir("Eski", packed, Tree.File("duz.bin", 800_000_000, Ctx.Now.AddDays(-400))));

        var unit = Assert.Single(UnitBuilder.Build(Ctx.Build(root), [new LargeOldFolderExtractor()]));

        Assert.True(UnitBuilder.CanCompress(unit));
        Assert.Equal(1_500_000_000, unit.CompressedBytes);
    }
}
