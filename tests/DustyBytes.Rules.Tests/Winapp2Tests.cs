using DustyBytes.Clean.Rules;

namespace DustyBytes.Rules.Tests;

public class Winapp2Tests
{
    static string FixturePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "sample-winapp2.ini");

    [Fact]
    public void ParsesBothSections()
    {
        var text = File.ReadAllText(FixturePath);
        var rules = Winapp2.Parse(text);
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void CapturesWarningAndActions()
    {
        var text = File.ReadAllText(FixturePath);
        var rules = Winapp2.Parse(text);
        var chrome = rules.Single(r => r.Name.StartsWith("Google Chrome"));
        var option = chrome.Options.Single();
        Assert.Contains("sıfırlayabilir", option.Warning);
        Assert.Contains(option.Actions, a => a.Type == CleanActionType.RegistryDelete);
        Assert.Contains(option.Actions, a => a.Type == CleanActionType.Delete);
    }

    [Fact]
    public void DetectFileBecomesRunningLockEvidence()
    {
        var text = File.ReadAllText(FixturePath);
        var rules = Winapp2.Parse(text);
        var sample = rules.Single(r => r.Name.StartsWith("Sample App"));
        Assert.Single(sample.Running.LockFiles);
        Assert.Contains("SampleApp", sample.Running.LockFiles[0]);
    }

    [Fact]
    public void SourceAttributionIsPresent()
    {
        var text = File.ReadAllText(FixturePath);
        var rules = Winapp2.Parse(text);
        Assert.All(rules, r => Assert.Contains("CC-BY-SA-4.0", r.Source));
    }
}
