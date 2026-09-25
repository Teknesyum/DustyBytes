namespace DustyBytes.Units.Tests;

public sealed class LargeFileListTests
{
    [Fact]
    public void ReturnsTopNFilesSortedBySizeDescending()
    {
        var root = Tree.Dir(@"C:\",
            Tree.File("small.bin", 10_000_000),
            Tree.Dir("Sub", Tree.File("huge.bin", 900_000_000), Tree.File("medium.bin", 300_000_000)));

        var entries = LargeFileList.Build(root, take: 2);

        Assert.Equal(2, entries.Count);
        Assert.Equal("huge.bin", Path.GetFileName(entries[0].Path));
        Assert.Equal("medium.bin", Path.GetFileName(entries[1].Path));
        Assert.True(entries[0].SizeBytes >= entries[1].SizeBytes);
    }

    [Fact]
    public void DirectoriesAreNeverIncluded()
    {
        var root = Tree.Dir(@"C:\", Tree.Dir("EmptyLookingDir", Tree.File("a.bin", 1_000_000)));

        var entries = LargeFileList.Build(root);

        Assert.All(entries, e => Assert.NotEqual("EmptyLookingDir", Path.GetFileName(e.Path)));
    }

    [Fact]
    public void BuildDoesNotMutateTheTree()
    {
        var file = Tree.File("a.bin", 5_000_000);
        var root = Tree.Dir(@"C:\", file);
        var sizeBefore = root.Size;

        LargeFileList.Build(root);

        Assert.Equal(sizeBefore, root.Size);
    }
}
