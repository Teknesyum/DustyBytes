using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;

namespace DustyBytes.Units.Tests;

public sealed class UnitBuilderTests
{
    [Fact]
    public void DevArtifactInsideFilmFolderWinsOverFilm()
    {
        var video = Tree.File("Big.mkv", 900_000_000);
        var nodeModules = Tree.Dir("node_modules", Tree.File("lib.js", 400_000_000));
        var folder = Tree.Dir("MovieProject", video, Tree.File("package.json", 200), nodeModules);
        var root = Tree.Dir(@"C:\", folder);

        var units = UnitBuilder.Build(Ctx.Build(root), [new DevArtifactExtractor(), new FilmExtractor()]);

        var unit = Assert.Single(units);
        Assert.Equal(UnitKind.DevArtifact, unit.Kind);
    }

    [Fact]
    public void ProtectedPathsAreDroppedExceptSystemArtifact()
    {
        var windows = Tree.Dir("Windows",
            Tree.Dir("Temp", Tree.File("a.tmp", 1_000_000)),
            Tree.Dir("SoftwareDistribution", Tree.Dir("Download", Tree.File("u.cab", 500_000_000))));
        var root = Tree.Dir(@"C:\", windows);

        var rules = new ProtectedRules { Roots = [new PathRule { Path = @"C:\Windows", Reason = "test" }] };
        var protectedList = new ProtectedList(rules);
        var ctx = Ctx.Build(root, protectedList: protectedList);

        var units = UnitBuilder.Build(ctx, [new CacheExtractor(), new SystemArtifactExtractor()]);

        Assert.DoesNotContain(units, u => u.Kind == UnitKind.Cache);
        Assert.Contains(units, u => u.Kind == UnitKind.SystemArtifact);
    }

    [Fact]
    public void CloudPlaceholderNodesAreExcluded()
    {
        var windows = Tree.Dir("Windows", Tree.Dir("Temp", Tree.File("a.tmp", 1_000_000)));
        windows.Child("Temp")!.Flags |= NodeFlags.CloudPlaceholder;
        var root = Tree.Dir(@"C:\", windows);

        var units = UnitBuilder.Build(Ctx.Build(root), [new CacheExtractor()]);

        Assert.Empty(units);
    }

    [Fact]
    public void LogScalingKeepsHugeOldFolderFromCrushingSmallUnusedGame()
    {
        var oldFolder = Tree.Dir("Archive", Tree.File("img.iso", 200L * 1024 * 1024 * 1024, Ctx.Now.AddDays(-1000)));
        var gameInstall = Tree.Dir("NeverPlayed", Tree.File("game.exe", 2L * 1024 * 1024 * 1024));
        var root = Tree.Dir(@"C:\", oldFolder, Tree.Dir("Games", gameInstall));

        var usage = new FakeUsageIndex();
        usage.GamesList.Add(new GameInstall
        {
            Launcher = "Steam",
            Id = "1",
            Name = "Never Played",
            InstallDir = gameInstall.FullPath,
        });

        var ctx = Ctx.Build(root, usage);
        var units = UnitBuilder.Build(ctx, [new LargeOldFolderExtractor(), new GameExtractor()]);

        var folderUnit = units.Single(u => u.Kind == UnitKind.Folder);
        var gameUnit = units.Single(u => u.Kind == UnitKind.Game);

        var sizeRatio = (double)folderUnit.SizeBytes / gameUnit.SizeBytes;
        var scoreRatio = folderUnit.Score / gameUnit.Score;

        Assert.True(sizeRatio > 50, $"boyut oranı beklenenden düşük: {sizeRatio}");
        Assert.True(scoreRatio < sizeRatio / 5, $"log etkisi görülmedi: boyut oranı {sizeRatio}, puan oranı {scoreRatio}");
        Assert.True(gameUnit.Score > 0);
    }

    [Fact]
    public void UnknownUsageSignalDoesNotZeroOutScore()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("Windows", Tree.Dir("Temp", Tree.File("a.tmp", 5_000_000))));

        var units = UnitBuilder.Build(Ctx.Build(root), [new CacheExtractor()]);

        var unit = Assert.Single(units);
        Assert.False(unit.Usage.IsKnown);
        Assert.True(unit.Score > 0);
    }
}
