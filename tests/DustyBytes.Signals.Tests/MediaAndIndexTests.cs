using Xunit.Abstractions;

namespace DustyBytes.Signals.Tests;

public class MediaAndIndexTests(ITestOutputHelper output)
{
    [Fact]
    public void Vlc_ParsesRecents()
    {
        var plays = VlcSignal.Parse(Fixture.Text("vlc-qt-interface.ini"));
        Assert.Equal(3, plays.Count);
        Assert.Equal(@"D:\Filmler\Uzak Diyar (2020).mkv", plays[0].FilePath);
        Assert.Equal(@"D:\Diziler\Dizi, Sezon 1\S01E02.mp4", plays[1].FilePath);
        Assert.Equal(@"E:\Video\çocukluk.avi", plays[2].FilePath);
        Assert.All(plays, p => Assert.Null(p.LastPlayed));
        Assert.All(plays, p => Assert.Equal("VLC", p.Source));
    }

    [Fact]
    public void Vlc_MissingSectionIsEmpty()
    {
        Assert.Empty(VlcSignal.Parse("[General]\na=b\n"));
        Assert.Empty(new VlcSignal(Path.Combine(Path.GetTempPath(), "yok-" + Guid.NewGuid(), "x.ini")).Read());
    }

    [Fact]
    public void MpcHc_ParsesIni()
    {
        var plays = MpcHc_Read();
        Assert.Equal(3, plays.Count);
        var film = plays.Single(p => p.FilePath == @"D:\Filmler\Film.mkv");
        Assert.Equal(new DateTimeOffset(2025, 3, 14, 20, 15, 30, 123, TimeSpan.Zero), film.LastPlayed);
        Assert.Null(plays.Single(p => p.FilePath == @"D:\Filmler\Eski.mp4").LastPlayed);
        Assert.Null(plays.Single(p => p.FilePath == @"E:\Klipler\Klip.mp4").LastPlayed);
    }

    private static IReadOnlyList<MediaPlay> MpcHc_Read() =>
        new MpcHcSignal([Fixture.Path("mpc-hc64.ini")], useRegistry: false).Read();

    [Fact]
    public void Index_PrefersNewestAndReportsErrors()
    {
        var t1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddDays(30);
        var index = UsageIndex.Collect(
            [
                new FakeLibrary([
                    new GameInstall { Launcher = "Steam", Id = "1", Name = "A", InstallDir = @"D:\Steam\common\A", LastPlayed = t1 },
                    new GameInstall { Launcher = "Epic", Id = "b", Name = "B", InstallDir = @"D:\Epic\B" },
                    new GameInstall { Launcher = "GOG", Id = "c", Name = "C", InstallDir = @"D:\GOG\C" },
                ]),
                new BrokenLibrary(),
            ],
            [
                new FakeExe("Prefetch", Reliability.Prefetch, [
                    new ExecutableRun(@"D:\Epic\B\bin\b.exe", t1, 4, "Prefetch"),
                    new ExecutableRun(@"C:\Tools\x.exe", t1, 2, "Prefetch"),
                    new ExecutableRun(@"D:\GOG\C\unins000.exe", t2, 1, "Prefetch"),
                ]),
                new FakeExe("UserAssist", Reliability.UserAssist, [
                    new ExecutableRun(@"d:\epic\b\bin\B.EXE", t2, 9, "UserAssist"),
                    new ExecutableRun(@"C:\Tools\x.exe", t1, 1, "UserAssist"),
                ]),
                new BrokenExe(),
            ],
            [new FakeMedia([new MediaPlay(@"D:\Film\a.mkv", t2, "VLC"), new MediaPlay(@"D:\Film\b.mkv", null, "VLC")])]);

        var x = index.ForExecutable(@"C:\Tools\x.exe");
        Assert.Equal("Prefetch", x.Source);
        Assert.Equal(Reliability.Prefetch, x.Reliability);

        var b = index.ForExecutable(@"D:\Epic\B\bin\b.exe");
        Assert.Equal(t2, b.LastUsed);
        Assert.Equal("UserAssist", b.Source);

        Assert.Equal(t2, index.ForFolder(@"D:\Epic").LastUsed);
        Assert.False(index.ForFolder(@"D:\GOG\C").IsKnown);
        Assert.False(index.ForFolder(@"E:\bos").IsKnown);
        Assert.False(index.ForExecutable(@"C:\yok.exe").IsKnown);

        var steamFolder = index.ForFolder(@"D:\Steam\common\A\bin");
        Assert.Equal("Steam son oynanma", steamFolder.Source);
        Assert.Equal(t1, steamFolder.LastUsed);

        var gameB = index.Games.Single(g => g.Id == "b");
        Assert.Equal(t2, gameB.LastPlayed);
        Assert.Contains(gameB.Executables, e => e.EndsWith("b.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Null(index.Games.Single(g => g.Id == "c").LastPlayed);
        Assert.Equal(t1, index.Games.Single(g => g.Id == "1").LastPlayed);

        Assert.Equal(t2, index.ForMedia(@"D:\Film\a.mkv").LastUsed);
        Assert.Equal(Reliability.MediaPlayer, index.ForMedia(@"D:\Film\a.mkv").Reliability);
        Assert.Equal("VLC", index.ForMedia(@"D:\Film\b.mkv").Source);
        Assert.False(index.ForMedia(@"D:\Film\b.mkv").IsKnown);

        Assert.Contains(index.Reports, r => r.Source == "Kırık" && r.Error is not null);
        Assert.Contains(index.Reports, r => r.Source == "KırıkExe" && r.Error is not null);
        Assert.Contains(@"D:\Steam", index.LibraryRoots);
    }

    [Fact]
    public void Index_CollectsRealMachine()
    {
        var index = UsageIndex.Collect();
        output.WriteLine($"oyun={index.Games.Count} exe={index.ExecutableCount} medya={index.MediaCount} kok={index.LibraryRoots.Count}");
        output.WriteLine($"oyun_son_oynanma_bilinen={index.Games.Count(g => g.LastPlayed is not null)}");
        var prefetch = new PrefetchSignal().Read();
        output.WriteLine($"prefetch_yol_diskte_var={prefetch.Count(r => File.Exists(r.ExePath))}/{prefetch.Count}");
        foreach (var r in index.Reports)
            output.WriteLine($"{r.Source}: {r.Count}{(r.Error is null ? "" : " hata: " + r.Error)}");
        Assert.NotNull(index.Reports);
    }

    private sealed class FakeLibrary(IReadOnlyList<GameInstall> games) : IGameLibrary
    {
        public string Launcher => "Sahte";
        public IReadOnlyList<string> LibraryRoots() => [@"D:\Steam"];
        public IReadOnlyList<GameInstall> ReadInstalls() => games;
    }

    private sealed class BrokenLibrary : IGameLibrary
    {
        public string Launcher => "Kırık";
        public IReadOnlyList<string> LibraryRoots() => throw new IOException("erişim yok");
        public IReadOnlyList<GameInstall> ReadInstalls() => throw new IOException("erişim yok");
    }

    private sealed class FakeExe(string source, double reliability, IReadOnlyList<ExecutableRun> runs) : IExecutableSignal
    {
        public string Source => source;
        public double Reliability => reliability;
        public IReadOnlyList<ExecutableRun> Read() => runs;
    }

    private sealed class BrokenExe : IExecutableSignal
    {
        public string Source => "KırıkExe";
        public double Reliability => 0.5;
        public IReadOnlyList<ExecutableRun> Read() => throw new UnauthorizedAccessException("yetki yok");
    }

    private sealed class FakeMedia(IReadOnlyList<MediaPlay> plays) : IMediaSignal
    {
        public string Source => "VLC";
        public IReadOnlyList<MediaPlay> Read() => plays;
    }
}
