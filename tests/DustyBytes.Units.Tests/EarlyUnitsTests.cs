using DustyBytes.Core.Model;
using DustyBytes.Units;

namespace DustyBytes.Units.Tests;

public class EarlyUnitsTests
{
    static Unit Make(UnitKind kind, params string[] paths) => new()
    {
        Id = string.Join("|", paths),
        Kind = kind,
        Name = "x",
        Paths = paths,
        SizeBytes = 1,
    };

    [Fact]
    public void Unit_Settles_Only_When_Its_Whole_Group_Is_Done()
    {
        var early = new EarlyUnits(@"C:\",
        [
            new EarlyRoot(@"C:\Users\a\Chrome", "tarayıcı:Chrome"),
            new EarlyRoot(@"C:\Users\b\Chrome", "tarayıcı:Chrome"),
            new EarlyRoot(@"C:\Program Files\P", @"C:\Program Files\P"),
        ]);
        var browser = Make(UnitKind.BrowserCache, @"C:\Users\a\Chrome\Default\Cache");
        var program = Make(UnitKind.Program, @"C:\Program Files\P");
        var outside = Make(UnitKind.Game, @"D:\Oyunlar\G", @"C:\steamapps\appmanifest_1.acf");
        var uncovered = Make(UnitKind.Cache, @"C:\Başka");

        Assert.False(early.IsSettled(browser));
        Assert.False(early.IsSettled(program));
        Assert.True(early.IsSettled(outside));
        Assert.False(early.IsSettled(uncovered));

        early.Done(@"C:\Users\a\Chrome");
        early.Done(@"C:\Program Files\P");
        Assert.False(early.IsSettled(browser));
        Assert.True(early.IsSettled(program));

        early.Done(@"C:\Users\b\Chrome");
        Assert.True(early.IsSettled(browser));
        Assert.False(early.IsSettled(uncovered));
    }

    [Fact]
    public void Draft_Drops_A_Cache_That_A_Dev_Artifact_Claims()
    {
        var proj = Tree.Dir("proj",
            Tree.File("MyApp.csproj", 2_000, Ctx.Now.AddDays(-40)),
            Tree.Dir("bin", Tree.File("MyApp.dll", 300_000_000, Ctx.Now.AddDays(-40))));
        var temp = Tree.Dir("Temp", proj, Tree.File("x.tmp", 1_000, Ctx.Now.AddDays(-40)));
        var root = Tree.Dir(@"C:", Tree.Dir("Users", Tree.Dir("a", Tree.Dir("AppData", Tree.Dir("Local", temp)))));
        var ctx = Ctx.Build(root);

        Assert.Contains(UnitBuilder.Build(ctx, EarlyUnits.Extractors), u => u.Kind == UnitKind.Cache);
        var draft = EarlyUnits.Build(ctx);

        Assert.DoesNotContain(draft, u => u.Kind is UnitKind.Cache or UnitKind.DevArtifact);
        Assert.Equal(
            UnitBuilder.Build(ctx).Where(u => u.Kind == UnitKind.Cache).Select(u => u.Id),
            draft.Where(u => u.Kind == UnitKind.Cache).Select(u => u.Id));
    }
}
