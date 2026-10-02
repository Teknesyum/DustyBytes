using DustyBytes.Clean.Rules;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Model;

namespace DustyBytes.Rules.Tests;

public class GuvenMetinleriTests
{
    static string CleanersDir => Path.Combine(AppContext.BaseDirectory, "cleaners");

    static IEnumerable<CleanerRule> LoadAll() =>
        Directory.EnumerateFiles(CleanersDir, "*.json").Select(CleanerRule.Load);

    [Fact]
    public void Every_Rule_Option_Has_All_Three_Fields()
    {
        var count = 0;
        foreach (var rule in LoadAll())
            foreach (var option in rule.Options)
            {
                count++;
                Assert.False(string.IsNullOrWhiteSpace(option.What), $"{rule.Id}/{option.Id}: Bu nedir? boş");
                Assert.False(string.IsNullOrWhiteSpace(option.IfDeleted), $"{rule.Id}/{option.Id}: Silersem ne olur? boş");
                Assert.False(string.IsNullOrWhiteSpace(option.Returns), $"{rule.Id}/{option.Id}: Geri gelir mi? boş");
                Assert.True(option.Explanation.IsComplete);
            }
        Assert.True(count > 20);
    }

    [Fact]
    public void Every_UnitKind_Has_A_Complete_Explanation()
    {
        foreach (var kind in Enum.GetValues<UnitKind>())
            Assert.True(UnitKindInfo.Explain(kind).IsComplete, kind.ToString());
    }

    [Fact]
    public void Winapp2_Options_Get_All_Three_Fields()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "sample-winapp2.ini"));
        var rules = Winapp2.Parse(text);
        Assert.NotEmpty(rules);
        foreach (var option in rules.SelectMany(r => r.Options))
            Assert.True(option.Explanation.IsComplete);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("edge")]
    [InlineData("brave")]
    [InlineData("opera")]
    [InlineData("firefox")]
    public void Browser_Cookies_Are_A_Separate_Sensitive_Option_With_Warning(string id)
    {
        var rule = LoadAll().Single(r => r.Id == id);
        var cookies = Assert.Single(rule.Options, o => o.Id == "cookies");
        Assert.True(cookies.Sensitive);
        Assert.True(cookies.TouchesSession);
        Assert.Equal("Sitelerden çıkış yapılır", cookies.Warning);
        foreach (var other in rule.Options.Where(o => o.Id != "cookies"))
            Assert.False(other.TouchesSession, $"{id}/{other.Id} oturum verisine dokunuyor");
    }

    [Theory]
    [InlineData("Cookies")]
    [InlineData("Login Data")]
    [InlineData("Web Data")]
    [InlineData("logins.json")]
    [InlineData("Local Storage")]
    public void Session_Heuristic_Catches_Session_Paths(string name)
    {
        var option = new CleanerOption
        {
            Id = "x",
            Label = "Başka bir ad",
            Actions = [new CleanAction { Type = CleanActionType.Delete, Path = @"%APPDATA%\App\" + name }],
        };
        Assert.True(option.TouchesSession);
    }

    [Theory]
    [InlineData(".NET Runtime 8.0.1 (x64)", "Microsoft Corporation", true)]
    [InlineData("Microsoft .NET Host - 8.0.1 (x64)", "Microsoft Corporation", true)]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.1 (x64)", "Microsoft Corporation", true)]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33135", "Microsoft Corporation", true)]
    [InlineData("Microsoft Visual C++ 2008 Redistributable - x86 9.0.30729.6161", "Microsoft Corporation", true)]
    [InlineData("Microsoft DirectX End-User Runtime", "Microsoft Corporation", true)]
    [InlineData("Java 8 Update 401", "Oracle Corporation", true)]
    [InlineData("Java(TM) SE Development Kit 17.0.10 (64-bit)", "Oracle Corporation", true)]
    [InlineData("Eclipse Temurin JRE with Hotspot 17.0.10+7 (x64)", "Eclipse Adoptium", true)]
    [InlineData("Microsoft Edge WebView2 Runtime", "Microsoft Corporation", true)]
    [InlineData("Microsoft Visual Studio Code", "Microsoft Corporation", false)]
    [InlineData("Visual Studio Community 2022", "Microsoft Corporation", false)]
    [InlineData("JavaScript Debugger", "Acme", false)]
    [InlineData("Oracle VM VirtualBox 7.0.14", "Oracle Corporation", false)]
    [InlineData("Spotify", "Spotify AB", false)]
    [InlineData("7-Zip 23.01 (x64)", "Igor Pavlov", false)]
    public void Shared_Runtimes_Are_Detected_By_Name_And_Publisher(string name, string publisher, bool expected)
    {
        Assert.Equal(expected, SharedRuntimes.IsShared(name, publisher));
    }

    [Fact]
    public void Framework_Packages_Are_Shared()
    {
        var program = new InstalledProgram { Id = "f", DisplayName = "Microsoft.VCLibs.140.00", IsFramework = true };
        Assert.True(SharedRuntimes.IsShared(program));
    }
}
