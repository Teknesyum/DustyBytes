using System.Reflection;
using DustyBytes.Clean.SystemCleanup;

namespace DustyBytes.Rules.Tests;

public class ComponentStoreCleanupTests
{
    static string FixtureText => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "dism-analyze-sample.txt"));

    [Fact]
    public void ParsesRecoverableSizeAndRecommendation()
    {
        var analysis = ComponentStoreCleanup.ParseAnalysis(FixtureText);
        Assert.True(analysis.Recommended);
        Assert.Equal((long)(842.50 * 1024 * 1024), analysis.RecoverableBytes);
    }

    [Fact]
    public void CleanupArgumentsNeverContainResetBase()
    {
        Assert.DoesNotContain("ResetBase", ComponentStoreCleanup.CleanupArguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResetBase", ComponentStoreCleanup.AnalyzeArguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StartComponentCleanup", ComponentStoreCleanup.CleanupArguments);
    }

    [Fact]
    public void NoPublicApiCanInjectResetBase()
    {
        var type = typeof(ComponentStoreCleanup);
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var parameter in method.GetParameters())
                Assert.DoesNotContain("reset", parameter.Name ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }
}
