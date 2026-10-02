using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class GuvenVarsayilanlariTests
{
    static CleanRuleRow Row(bool running, params CleanerOption[] options) =>
        new(new CleanRuleInfo(new CleanerRule { Id = "chrome", Name = "Chrome", Options = [.. options] }, running, null), () => { });

    static CleanerOption Cookies() => new()
    {
        Id = "cookies",
        Label = "Çerezleri de sil",
        Warning = "Sitelerden çıkış yapılır",
        Sensitive = true,
        Actions = [new CleanAction { Type = CleanActionType.Delete, Path = @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Network\Cookies" }],
    };

    static CleanerOption Cache() => new()
    {
        Id = "cache",
        Label = "Önbellek",
        Actions = [new CleanAction { Type = CleanActionType.Delete, Path = @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache" }],
    };

    [Fact]
    public void Cookie_Option_Is_Unchecked_By_Default_And_Cache_Is_Checked()
    {
        var row = Row(false, Cache(), Cookies());
        Assert.True(row.Options.Single(o => o.Id == "cache").IsChecked);
        Assert.False(row.Options.Single(o => o.Id == "cookies").IsChecked);
    }

    [Fact]
    public void Session_Looking_Option_Is_Never_Safe_Even_Without_Flag_Or_Warning()
    {
        var login = new CleanerOption
        {
            Id = "login",
            Label = "Kayıtlı veriler",
            Actions = [new CleanAction { Type = CleanActionType.Delete, Path = @"%APPDATA%\App\Login Data" }],
        };
        var info = new CleanRuleInfo(new CleanerRule { Id = "x", Name = "X", Options = [login] }, false, null);
        Assert.False(CleanRuleRow.Safe(info, login));
        Assert.True(CleanRuleRow.Safe(info, Cache()));
    }

    [Fact]
    public void Session_Note_Shows_Until_Cookies_Are_Chosen_And_Warning_Follows_The_Choice()
    {
        var row = Row(false, Cache(), Cookies());
        var cookies = row.Options.Single(o => o.Id == "cookies");
        Assert.True(row.ShowSessionNote);
        Assert.Equal("Giriş yaptığın siteler açık kalır", row.SessionNote);
        Assert.False(cookies.ShowWarning);

        cookies.IsChecked = true;
        Assert.False(row.ShowSessionNote);
        Assert.True(cookies.ShowWarning);
        Assert.Equal("Sitelerden çıkış yapılır", cookies.Warning);
    }

    [Fact]
    public void Shared_Runtime_Rows_Start_Unchecked_And_Carry_The_Note()
    {
        var program = FakeBackend.Program("vc", "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", 20_000_000);
        var row = new ProgramRow(new ProgramInfo(program, UsageSignal.Unknown), DateTimeOffset.Now);
        Assert.False(row.IsChecked);
        Assert.True(row.IsShared);
        Assert.Equal("Başka programlar bunu kullanıyor olabilir", row.SharedNote);

        var plain = new ProgramRow(new ProgramInfo(FakeBackend.Program("s", "Spotify", "Spotify AB", 1), UsageSignal.Unknown), DateTimeOffset.Now);
        Assert.False(plain.IsShared);
        Assert.Equal("", plain.SharedNote);
    }
}
