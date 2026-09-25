using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Signals;

namespace DustyBytes.Units.Tests;

public sealed class KindBreakdownTests
{
    [Fact]
    public void GameUnderLauncherLibraryRootSurvivesDefaultBuild()
    {
        var game = Tree.Dir("BigGame", Tree.File("game.exe", 40_000_000_000), Tree.Dir("bin", Tree.File("x.dll", 1_000_000)), Tree.File("game.csproj", 100));
        var library = Tree.Dir("Steam", Tree.Dir("steamapps", Tree.Dir("common", game)));
        var root = Tree.Dir(@"C:\", Tree.Dir("Games", library));

        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall { Launcher = "Steam", Id = "1", Name = "Big Game", InstallDir = game.FullPath, UninstallUri = "steam://uninstall/1" });
        var protectedList = new ProtectedList(new ProtectedRules());
        protectedList.AddLauncherLibrary(library.FullPath, "Steam");

        var units = UnitBuilder.Build(Ctx.Build(root, usage, protectedList));

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.Game, unit.Kind);
        Assert.Equal(RemovalMethod.Launcher, unit.Removal);
    }

    [Fact]
    public void LauncherLibraryStillBlocksQuarantineUnits()
    {
        var video = Tree.File("Clip.mkv", 5_000_000_000, Ctx.Now.AddYears(-3));
        var library = Tree.Dir("Steam", Tree.Dir("Movies", video));
        var root = Tree.Dir(@"C:\", Tree.Dir("Games", library));
        var protectedList = new ProtectedList(new ProtectedRules());
        protectedList.AddLauncherLibrary(library.FullPath, "Steam");

        var units = UnitBuilder.Build(Ctx.Build(root, protectedList: protectedList));

        Assert.DoesNotContain(units, u => u.Paths.Any(p => p.StartsWith(library.FullPath, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void InstalledProgramsBecomeProgramUnits()
    {
        var app = Tree.Dir("BigApp", Tree.File("app.exe", 3_000_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Program Files", app));
        var ctx = Ctx.Build(root) with
        {
            Programs = [new ProgramInstall("reg:bigapp", "Big App", app.FullPath), new ProgramInstall("reg:ghost", "Ghost", @"C:\Program Files\Missing"), new ProgramInstall("reg:rel", "Relative", "BigApp")],
        };

        var units = UnitBuilder.Build(ctx);

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.Program, unit.Kind);
        Assert.Equal(RemovalMethod.Uninstaller, unit.Removal);
        Assert.Equal("Big App", unit.Name);
        Assert.Equal(3_000_000_000, unit.SizeBytes);
    }

    [Fact]
    public void GameWinsOverProgramForSameFolder()
    {
        var game = Tree.Dir("Shared", Tree.File("g.exe", 2_000_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Games", game));
        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall { Launcher = "Epic", Id = "e", Name = "Shared Game", InstallDir = game.FullPath });
        var ctx = Ctx.Build(root, usage) with { Programs = [new ProgramInstall("reg:s", "Shared Game", game.FullPath)] };

        var units = UnitBuilder.Build(ctx);

        Assert.Equal(UnitKind.Game, Assert.Single(units).Kind);
    }
}
