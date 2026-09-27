using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units.Tests;

public sealed class KnownContentExtractorTests
{
    const long GB = 1L << 30;

    static ScanNode Profile(params ScanNode[] children) =>
        Tree.Dir(@"C:\", Tree.Dir("Users", Tree.Dir("alice", children)));

    [Fact]
    public void LmStudioModelsBecomeNamedUnitsWithPlainEffect()
    {
        var root = Profile(Tree.Dir(".lmstudio", Tree.Dir("models", Tree.Dir("lmstudio-community",
            Tree.Dir("Llama-3.3-70B-Instruct-GGUF", Tree.File("model.gguf", 40 * GB)),
            Tree.Dir("tiny-model", Tree.File("tiny.gguf", 300_000_000))))));

        var unit = new KnownContentExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal(UnitKind.AppContent, unit.Kind);
        Assert.Equal("Llama-3.3-70B-Instruct-GGUF", unit.Name);
        Assert.Equal("LM Studio · Dil modeli", unit.Label);
        Assert.Equal(40 * GB, unit.SizeBytes);
        Assert.Equal(RemovalMethod.Quarantine, unit.Removal);
        Assert.Contains("yeniden indirirsiniz", unit.Effect);
    }

    [Fact]
    public void HuggingFaceRepoNameIsReadable()
    {
        var root = Profile(Tree.Dir(".cache", Tree.Dir("huggingface", Tree.Dir("hub",
            Tree.Dir("models--meta-llama--Llama-3.1-8B", Tree.File("blob", 16 * GB))))));

        var unit = new KnownContentExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal("meta-llama/Llama-3.1-8B", unit.Name);
    }

    [Fact]
    public void AggregateItemSumsPatternsPerUserAndSkipsSmall()
    {
        var root = Profile(
            Tree.Dir(".nuget", Tree.Dir("packages", Tree.File("a.nupkg", GB))),
            Tree.Dir("AppData", Tree.Dir("Local",
                Tree.Dir("NuGet", Tree.Dir("v3-cache", Tree.File("b", GB / 2))),
                Tree.Dir("npm-cache", Tree.File("c", 200_000_000)))));

        var units = new KnownContentExtractor().Extract(Ctx.Build(root)).ToList();

        var nuget = Assert.Single(units);
        Assert.Equal("NuGet · Paket önbelleği", nuget.Label);
        Assert.Equal("NuGet paket önbelleği", nuget.Name);
        Assert.Equal(GB + GB / 2, nuget.SizeBytes);
        Assert.Equal(2, nuget.Paths.Count);
    }

    [Fact]
    public void AndroidVirtualDeviceNameDropsSuffix()
    {
        var root = Profile(Tree.Dir(".android", Tree.Dir("avd",
            Tree.Dir("Pixel_6_API_34.avd", Tree.File("userdata.img", 6 * GB)))));

        var unit = new KnownContentExtractor().Extract(Ctx.Build(root)).Single();

        Assert.Equal("Pixel 6 API 34", unit.Name);
    }

    [Fact]
    public void KnownContentOutranksLargeOldFolderForSamePath()
    {
        var old = Ctx.Now.AddYears(-2);
        var root = Profile(Tree.Dir(".lmstudio", Tree.Dir("models", Tree.Dir("pub",
            Tree.Dir("Big-Model", Tree.File("m.gguf", 20 * GB, old))))));

        var units = UnitBuilder.Build(Ctx.Build(root));

        Assert.Contains(units, u => u.Kind == UnitKind.AppContent && u.Name == "Big-Model");
        Assert.DoesNotContain(units, u => u.Kind == UnitKind.Folder && u.Paths.Any(p => p.Contains(".lmstudio", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CatalogLoadsAndEveryItemSpeaksPlainly()
    {
        var rules = KnownContentRules.LoadDefault();
        Assert.NotEmpty(rules.Items);
        Assert.All(rules.Items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Owner));
            Assert.False(string.IsNullOrWhiteSpace(i.What));
            Assert.False(string.IsNullOrWhiteSpace(i.Effect));
            Assert.NotEmpty(i.Patterns);
            Assert.True(i.MinBytes >= 1L << 30);
        });
    }
}
