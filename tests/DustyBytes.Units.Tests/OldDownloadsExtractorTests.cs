using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;

namespace DustyBytes.Units.Tests;

public sealed class OldDownloadsExtractorTests
{
    static readonly DateTimeOffset Old = Ctx.Now.AddDays(-200);

    static ScanNode Root(params ScanNode[] files) =>
        Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice", Tree.Dir("Downloads", files))));

    [Fact]
    public void Uzun_Suredir_Acilmayan_Dosyalar_Tek_Birimde_Toplanir()
    {
        var root = Root(
            Tree.File("foto.jpg", 5_000_000, Old),
            Tree.File("rapor.pdf", 1_000_000, Ctx.Now.AddDays(-10)),
            Tree.File("video.mp4", 9_000_000, Old),
            Tree.File("sarki.mp3", 3_000_000, Old),
            Tree.File("bos.txt", 0, Old),
            Tree.Dir("klasor", Tree.File("ic.txt", 100, Old)));
        var usage = new FakeUsageIndex();
        usage.Folders[@"C:\Users\alice\Downloads\video.mp4"] = new UsageSignal(Ctx.Now.AddDays(-5), "test", 0.9);
        var ctx = Ctx.Build(root, usage) with
        {
            Opened = p => p.EndsWith("sarki.mp3", StringComparison.OrdinalIgnoreCase) ? Ctx.Now.AddDays(-20) : null,
        };

        var unit = Assert.Single(new OldDownloadsExtractor().Extract(ctx));

        Assert.Equal(UnitKind.OldDownload, unit.Kind);
        Assert.Equal(OldDownloadsExtractor.Title, unit.Name);
        Assert.Equal([@"C:\Users\alice\Downloads\foto.jpg"], unit.Paths);
        Assert.Equal(5_000_000, unit.SizeBytes);
        Assert.True(unit.ContainsUserData);
        Assert.Equal(RemovalMethod.Quarantine, unit.Removal);
        Assert.StartsWith("OldDownload-", unit.Id);
        Assert.Contains("kalıcı silinmez", unit.Effect);
    }

    [Fact]
    public void Yeni_Dosyalar_Birim_Olusturmaz()
    {
        var root = Root(Tree.File("yeni.jpg", 5_000_000, Ctx.Now.AddDays(-30)));
        Assert.Empty(new OldDownloadsExtractor().Extract(Ctx.Build(root)));
    }

    [Fact]
    public void Kurulum_Dosyasi_Ve_Korunan_Dosya_Gruptan_Dusulur()
    {
        var root = Root(
            Tree.File("setup.exe", 40_000_000, Old),
            Tree.File("foto.jpg", 5_000_000, Old),
            Tree.File("gizli.jpg", 7_000_000, Old));
        var list = new ProtectedList(new ProtectedRules { Files = [new NameRule { Name = "gizli.jpg", Reason = "Test" }] });

        var units = UnitBuilder.Build(Ctx.Build(root, protectedList: list));

        var installer = Assert.Single(units, u => u.Kind == UnitKind.Installer);
        Assert.Equal("setup.exe", installer.Name);
        var old = Assert.Single(units, u => u.Kind == UnitKind.OldDownload);
        Assert.Equal([@"C:\Users\alice\Downloads\foto.jpg"], old.Paths);
        Assert.Equal(5_000_000, old.SizeBytes);
        Assert.Equal(OldDownloadsExtractor.ReasonFor(1), old.Reason);
    }
}
