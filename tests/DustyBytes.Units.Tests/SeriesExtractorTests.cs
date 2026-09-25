using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class SeriesExtractorTests
{
    [Fact]
    public void SxxEyyPatternIsDetectedAndSeasonListedInReason()
    {
        var season1 = Tree.Dir("Season 1",
            Tree.File("Show.S01E01.mkv", 400_000_000),
            Tree.File("Show.S01E02.mkv", 400_000_000));
        var show = Tree.Dir("Show", season1);
        var root = Tree.Dir(@"C:\", Tree.Dir("Series", show));

        var unit = new SeriesExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(UnitKind.Series, unit.Kind);
        Assert.Equal(show.FullPath, unit.Paths.Single());
        Assert.Contains("sezon 1", unit.Reason);
    }

    [Fact]
    public void NxNPatternIsDetectedWithoutSeasonFolder()
    {
        var show = Tree.Dir("OldShow",
            Tree.File("OldShow.1x01.avi", 350_000_000),
            Tree.File("OldShow.1x02.avi", 350_000_000));
        var root = Tree.Dir(@"C:\", show);

        var unit = new SeriesExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(show.FullPath, unit.Paths.Single());
    }

    [Fact]
    public void PlainNameWithoutEpisodePatternIsNotASeries()
    {
        var folder = Tree.Dir("Serie", Tree.File("Serie.Something.mkv", 400_000_000));
        var root = Tree.Dir(@"C:\", folder);

        var units = new SeriesExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }

    [Fact]
    public void ResolutionLikeNumbersDoNotMatchEpisodePattern()
    {
        var folder = Tree.Dir("Demo", Tree.File("Demo.1920x1080.mkv", 400_000_000));
        var root = Tree.Dir(@"C:\", folder);

        var units = new SeriesExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
