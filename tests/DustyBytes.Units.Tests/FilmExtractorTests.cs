using DustyBytes.Core.Model;

namespace DustyBytes.Units.Tests;

public sealed class FilmExtractorTests
{
    [Fact]
    public void SingleBigVideoWithCompanionsIsFilm()
    {
        var folder = Tree.Dir("Inception.2010.1080p",
            Tree.File("Inception.2010.1080p.mkv", 8_000_000_000),
            Tree.File("Inception.2010.1080p.srt", 50_000),
            Tree.File("poster.jpg", 200_000),
            Tree.File("movie.nfo", 1_000));
        var root = Tree.Dir(@"C:\", Tree.Dir("Films", folder));

        var unit = new FilmExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(UnitKind.Film, unit.Kind);
        Assert.Equal(RemovalMethod.Quarantine, unit.Removal);
        Assert.Equal(8_000_251_000, unit.SizeBytes);
        Assert.True(unit.Confidence > 0.6);
    }

    [Fact]
    public void SmallVideoIsNotAFilm()
    {
        var folder = Tree.Dir("Clip", Tree.File("clip.mp4", 50_000_000));
        var root = Tree.Dir(@"C:\", folder);

        var units = new FilmExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }

    [Fact]
    public void MultipleBigVideosInFolderAreNotTreatedAsFilm()
    {
        var folder = Tree.Dir("Show",
            Tree.File("ep1.mkv", 800_000_000),
            Tree.File("ep2.mkv", 800_000_000));
        var root = Tree.Dir(@"C:\", folder);

        var units = new FilmExtractor().Extract(Ctx.Build(root)).ToList();

        Assert.Empty(units);
    }
}
