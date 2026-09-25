using DustyBytes.Clean.Rules;

namespace DustyBytes.Rules.Tests;

public class PathGlobTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "dustybytes-test-" + Guid.NewGuid().ToString("N"));

    public PathGlobTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Profiles", "p1", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "Profiles", "p2", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "Profiles", "p1", "other"));
        File.WriteAllText(Path.Combine(_root, "Profiles", "p1", "parent.lock"), "");
        File.WriteAllText(Path.Combine(_root, "thumbcache_100.db"), "x");
        File.WriteAllText(Path.Combine(_root, "thumbcache_256.db"), "x");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ResolveDirectoriesExpandsWildcardSegment()
    {
        var pattern = Path.Combine(_root, "Profiles", "*", "cache2");
        var found = PathGlob.ResolveDirectories(pattern).ToList();
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void ResolveFilesMatchesGlobPattern()
    {
        var pattern = Path.Combine(_root, "thumbcache_*.db");
        var found = PathGlob.ResolveFiles(pattern).ToList();
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void ResolveFilesForLockPatternFindsParentLock()
    {
        var pattern = Path.Combine(_root, "Profiles", "*", "parent.lock");
        var found = PathGlob.ResolveFiles(pattern).ToList();
        Assert.Single(found);
    }

    [Fact]
    public void NoWildcardReturnsExactPath()
    {
        var pattern = Path.Combine(_root, "other.txt");
        var found = PathGlob.ResolveFiles(pattern).ToList();
        Assert.Single(found);
    }
}
