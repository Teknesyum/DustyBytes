namespace DustyBytes.Signals.Tests;

public class LibraryTests
{
    [Fact]
    public void Vdf_ParsesNestedAndEscapes()
    {
        var node = Vdf.Parse("\"Root\"\n{\n // yorum\n \"a\" \"C:\\\\x\\\\y\"\n \"sub\" { \"k\" \"v\" }\n}");
        Assert.Equal("Root", node.Name);
        Assert.Equal(@"C:\x\y", node.Get("a"));
        Assert.Equal("v", node["sub"]!.Get("k"));
        Assert.Equal("v", node["SUB"]!.Get("K"));
    }

    [Fact]
    public void LibraryFolders_ModernFormat()
    {
        var libs = SteamLibrary.ParseLibraryFolders(Fixture.Text("libraryfolders.vdf"));
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], libs);
    }

    [Fact]
    public void LibraryFolders_LegacyFormat()
    {
        var libs = SteamLibrary.ParseLibraryFolders(Fixture.Text("libraryfolders_legacy.vdf"));
        Assert.Equal([@"E:\Games\Steam"], libs);
    }

    [Fact]
    public void AppManifest_ReadsFields()
    {
        var game = SteamLibrary.ParseAppManifest(Fixture.Text("appmanifest_1091500.acf"), @"D:\SteamLibrary")!;
        Assert.Equal("1091500", game.Id);
        Assert.Equal("Cyberpunk 2077", game.Name);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Cyberpunk 2077", game.InstallDir);
        Assert.Equal(70368744177L, game.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1714567890), game.LastPlayed);
        Assert.Equal("steam://uninstall/1091500", game.UninstallUri);
    }

    [Fact]
    public void AppManifest_ZeroLastPlayedIsNull()
    {
        var game = SteamLibrary.ParseAppManifest(Fixture.Text("appmanifest_730.acf"), @"D:\SteamLibrary")!;
        Assert.Null(game.LastPlayed);
    }

    [Fact]
    public void SteamLibrary_ReadsFakeTree()
    {
        var root = Fixture.TempDir();
        try
        {
            var steam = Path.Combine(root, "Steam");
            var lib2 = Path.Combine(root, "Lib2");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
            var vdf = $"\"libraryfolders\"\n{{\n \"0\" {{ \"path\" \"{steam.Replace(@"\", @"\\")}\" }}\n \"1\" {{ \"path\" \"{lib2.Replace(@"\", @"\\")}\" }}\n}}";
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), vdf);
            File.Copy(Fixture.Path("appmanifest_730.acf"), Path.Combine(steam, "steamapps", "appmanifest_730.acf"));
            File.Copy(Fixture.Path("appmanifest_1091500.acf"), Path.Combine(lib2, "steamapps", "appmanifest_1091500.acf"));

            var lib = new SteamLibrary(steam);
            var games = lib.ReadInstalls();
            Assert.Equal(2, games.Count);
            Assert.Contains(games, g => g.Id == "1091500" && g.InstallDir.StartsWith(lib2, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.Combine(lib2, "steamapps", "appmanifest_1091500.acf"), games.Single(g => g.Id == "1091500").Manifest);
            Assert.Equal(2, lib.LibraryRoots().Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EpicItem_ReadsFields()
    {
        var game = EpicLibrary.ParseItem(Fixture.Text("epic_game.item"))!;
        Assert.Equal("Fortnite", game.Id);
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal(@"D:\Epic Games\Fortnite", game.InstallDir);
        Assert.Equal(45678901234L, game.SizeOnDisk);
        Assert.Null(game.LastPlayed);
        Assert.Equal(@"D:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe", Assert.Single(game.Executables));
        Assert.NotNull(game.UninstallUri);
    }

    [Fact]
    public void EpicItem_SkipsIncompleteAndDlc()
    {
        Assert.Null(EpicLibrary.ParseItem("{\"bIsIncompleteInstall\":true,\"AppName\":\"a\",\"InstallLocation\":\"D:\\\\x\"}"));
        Assert.Null(EpicLibrary.ParseItem("{\"AppName\":\"dlc\",\"MainGameAppName\":\"base\",\"InstallLocation\":\"D:\\\\x\"}"));
    }

    [Fact]
    public void EpicLibrary_ReadsManifestDir()
    {
        var dir = Fixture.TempDir();
        try
        {
            File.Copy(Fixture.Path("epic_game.item"), Path.Combine(dir, "ABC.item"));
            File.WriteAllText(Path.Combine(dir, "bozuk.item"), "{ bozuk");
            Assert.Equal(Path.Combine(dir, "ABC.item"), Assert.Single(new EpicLibrary(dir).ReadInstalls()).Manifest);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
