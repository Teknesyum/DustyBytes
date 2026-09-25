using DustyBytes.Core.Model;
using DustyBytes.Signals;

namespace DustyBytes.Units.Tests;

public sealed class GameExtractorTests
{
    [Fact]
    public void GameWithSaveDirContainsUserDataAndUsesLauncherRemoval()
    {
        var saveDir = Tree.Dir("Saves", Tree.File("save1.dat", 10_000));
        var install = Tree.Dir("BigGame", Tree.File("game.exe", 40_000_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Games", install), Tree.Dir("Users", Tree.Dir("alice", Tree.Dir("Saved Games", saveDir))));

        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall
        {
            Launcher = "Steam",
            Id = "123",
            Name = "Big Game",
            InstallDir = install.FullPath,
            SizeOnDisk = 40_000_000_000,
            LastPlayed = Ctx.Now.AddMonths(-14),
            UninstallUri = "steam://uninstall/123",
            SaveDirs = [saveDir.FullPath],
        });

        var unit = new GameExtractor().Extract(Ctx.Build(root, usage)).Single();

        Assert.Equal(UnitKind.Game, unit.Kind);
        Assert.Equal(RemovalMethod.Launcher, unit.Removal);
        Assert.Equal("steam://uninstall/123", unit.LauncherUri);
        Assert.True(unit.ContainsUserData);
        Assert.Contains(saveDir.FullPath, unit.Paths);
        Assert.Contains("Son oynanma", unit.Reason);
    }

    [Fact]
    public void GameWithoutSizeSignalFallsBackToScanTree()
    {
        var install = Tree.Dir("SmallGame", Tree.File("game.exe", 500_000_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Games", install));

        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall
        {
            Launcher = "GOG",
            Id = "g1",
            Name = "Small Game",
            InstallDir = install.FullPath,
        });

        var unit = new GameExtractor().Extract(Ctx.Build(root, usage)).Single();

        Assert.Equal(500_000_000, unit.SizeBytes);
        Assert.False(unit.ContainsUserData);
    }

    [Fact]
    public void ZeroSizeGameIsSkipped()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Games"));
        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall
        {
            Launcher = "Epic",
            Id = "e1",
            Name = "Ghost Game",
            InstallDir = @"C:\Games\Missing",
        });

        var units = new GameExtractor().Extract(Ctx.Build(root, usage)).ToList();

        Assert.Empty(units);
    }
}
