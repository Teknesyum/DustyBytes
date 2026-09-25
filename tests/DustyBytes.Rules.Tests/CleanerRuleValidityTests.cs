using DustyBytes.Clean.Rules;

namespace DustyBytes.Rules.Tests;

public class CleanerRuleValidityTests
{
    static readonly string[] ForbiddenBrowserTerms = ["History", "Cookies", "Login Data", "Web Data", "Bookmarks"];
    static readonly string[] BrowserRuleIds = ["chrome", "edge", "brave", "opera", "firefox"];

    static string CleanersDir => Path.Combine(AppContext.BaseDirectory, "cleaners");

    static IEnumerable<CleanerRule> LoadAll() =>
        Directory.EnumerateFiles(CleanersDir, "*.json").Select(CleanerRule.Load);

    [Fact]
    public void AllRuleFilesParse()
    {
        var rules = LoadAll().ToList();
        Assert.True(rules.Count >= 15, $"En az 15 kural bekleniyordu, {rules.Count} bulundu");
    }

    [Fact]
    public void RuleIdsAreUnique()
    {
        var ids = LoadAll().Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryRuleHasAtLeastOneOptionWithAction()
    {
        foreach (var rule in LoadAll())
        {
            Assert.NotEmpty(rule.Options);
            foreach (var option in rule.Options)
                Assert.NotEmpty(option.Actions);
        }
    }

    [Fact]
    public void BrowserRulesNeverTouchSensitiveData()
    {
        foreach (var rule in LoadAll().Where(r => BrowserRuleIds.Contains(r.Id)))
        {
            foreach (var option in rule.Options)
            foreach (var action in option.Actions)
            {
                var path = action.Path ?? action.Key ?? "";
                foreach (var forbidden in ForbiddenBrowserTerms)
                    Assert.DoesNotContain(forbidden, path, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void OptionIdsAreUniqueWithinRule()
    {
        foreach (var rule in LoadAll())
        {
            var ids = rule.Options.Select(o => o.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
