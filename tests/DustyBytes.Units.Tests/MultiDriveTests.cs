using DustyBytes.Core.Model;
using DustyBytes.Scan;
using DustyBytes.Signals;

namespace DustyBytes.Units.Tests;

public sealed class MultiDriveTests
{
    static Unit U(string id, string path, double score = 1) =>
        new() { Id = id, Kind = UnitKind.Folder, Name = id, Paths = [path], SizeBytes = 1_000_000_000, Score = score };

    [Fact]
    public void MergeKeepsEachUnitOnceFromItsOwnDrive()
    {
        var game = U("game", @"D:\SteamLibrary\steamapps\common\BigGame", 9);
        List<IReadOnlyList<Unit>> parts =
        [
            [game, U("c", @"C:\Eski", 3), U("e", @"E:\Kayip", 2)],
            [game with { Score = 1 }, U("d", @"D:\Filmler", 5), U("c", @"C:\Eski", 3)],
        ];

        var merged = UnitBuilder.Merge([@"C:\", @"D:\"], parts);

        Assert.Equal(["d", "c", "e", "game"], merged.Select(u => u.Id));
        Assert.Equal(1, merged.Single(u => u.Id == "game").Score);
    }

    [Fact]
    public void OwnerPicksLongestContainingRoot()
    {
        Assert.Equal(1, UnitBuilder.Owner([@"C:\", @"D:\"], U("x", @"d:\oyun")));
        Assert.Equal(0, UnitBuilder.Owner([@"C:\", @"D:\"], U("x", @"C:\oyun")));
        Assert.Equal(-1, UnitBuilder.Owner([@"C:\", @"D:\"], U("x", @"E:\oyun")));
    }

    [Fact]
    public void LauncherGameOnSecondDriveAppearsOnce()
    {
        var install = Tree.Dir("BigGame", Tree.File("game.exe", 40_000_000_000));
        var manifest = Tree.File("appmanifest_123.acf", 1_000);
        var d = Tree.Dir(@"D:\", Tree.Dir("SteamLibrary", Tree.Dir("steamapps", manifest, Tree.Dir("common", install))));
        var c = Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice", Tree.File("notes.txt", 1_000))));
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
            Manifest = manifest.FullPath,
        });

        var units = UnitBuilder.BuildDrives(Ctx.Build(c, usage), [new ScanResult { Root = c }, new ScanResult { Root = d }]);

        var game = Assert.Single(units, u => u.Kind == UnitKind.Game);
        Assert.Equal(@"D:\", game.Drive);
        Assert.DoesNotContain(units, u => u.Kind != UnitKind.Game && u.Paths.Any(p => install.FullPath.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SystemArtifactsComeOnlyFromSystemDrive()
    {
        ScanNode Drive(string root) => Tree.Dir(root,
            Tree.Dir("Windows.old", Tree.File("big.bin", 20_000_000_000)),
            Tree.Dir("Windows", Tree.Dir("SoftwareDistribution", Tree.Dir("Download", Tree.File("update.cab", 500_000_000)))));
        var c = Drive(@"C:\");
        var d = Drive(@"D:\");

        var units = UnitBuilder.BuildDrives(Ctx.Build(c), [new ScanResult { Root = c }, new ScanResult { Root = d }]);

        var system = units.Where(u => u.Kind == UnitKind.SystemArtifact).ToList();
        Assert.NotEmpty(system);
        Assert.All(system, u => Assert.Equal(@"C:\", u.Drive));
    }

    [Fact]
    public void UnitCarriesItsDrive()
    {
        Assert.Equal(@"D:\", U("x", @"d:\Filmler\a.mkv").Drive);
        Assert.Equal("", new Unit { Id = "y", Kind = UnitKind.Folder, Name = "y", Paths = [], SizeBytes = 0 }.Drive);
    }
}
