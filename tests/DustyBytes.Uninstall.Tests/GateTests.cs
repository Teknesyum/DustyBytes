using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class GateTests
{
    static RegKeyRef Lm(string path, RegView view = RegView.Registry64) => new(RegHive.LocalMachine, view, path);

    [Theory]
    [InlineData(@"SOFTWARE\Classes\.txt")]
    [InlineData(@"SOFTWARE\Classes\CLSID")]
    [InlineData(@"SOFTWARE\Classes\*\shellex\ContextMenuHandlers")]
    [InlineData(@"SOFTWARE\Classes\txtfile")]
    [InlineData(@"SOFTWARE\Microsoft\Office")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(@"SOFTWARE\Policies\Zqxv")]
    [InlineData(@"SOFTWARE\Windows\Zqxv")]
    [InlineData(@"SOFTWARE\RegisteredApplications")]
    [InlineData(@"SOFTWARE\WOW6432Node")]
    [InlineData(@"SOFTWARE")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\EventLog")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Zqxv")]
    public void BlockedKeys(string path) => Assert.NotNull(RegistryGate.KeyBlock(Lm(path)));

    [Theory]
    [InlineData(@"SOFTWARE\Zqxv Ltd\Widget")]
    [InlineData(@"SOFTWARE\Zqxv Ltd")]
    [InlineData(@"SOFTWARE\Classes\Zqxv.Document")]
    [InlineData(@"SOFTWARE\Classes\CLSID\{12345678-1234-1234-1234-123456789ABC}")]
    [InlineData(@"SOFTWARE\Classes\*\shellex\ContextMenuHandlers\ZqxvMenu")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ZqxvWidget_is1")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\zqxv.exe")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\ZqxvSvc")]
    public void AllowedKeys(string path) => Assert.Null(RegistryGate.KeyBlock(Lm(path)));

    [Fact]
    public void Wow64AndUserHivesAreJudgedByTheirLogicalPath()
    {
        Assert.NotNull(RegistryGate.KeyBlock(Lm(@"SOFTWARE\WOW6432Node\Microsoft\Office", RegView.Registry32)));
        Assert.Null(RegistryGate.KeyBlock(Lm(@"SOFTWARE\WOW6432Node\Zqxv Ltd\Widget", RegView.Registry32)));
        Assert.NotNull(RegistryGate.KeyBlock(new RegKeyRef(RegHive.Users, RegView.Registry64, @"S-1-5-21-1-2-3-1001_Classes\.txt")));
        Assert.Null(RegistryGate.KeyBlock(new RegKeyRef(RegHive.Users, RegView.Registry64, @"S-1-5-21-1-2-3-1001\Software\Zqxv")));
        Assert.NotNull(RegistryGate.KeyBlock(new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer")));
    }

    [Fact]
    public void ValuesAreAllowedOnlyInKnownPlaces()
    {
        Assert.Null(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "Zqxv"));
        Assert.Null(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Classes\.zqxv"), ""));
        Assert.Null(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Classes\.zqxv\OpenWithProgids"), "Zqxv.Doc"));
        Assert.NotNull(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Classes\.zqxv\ShellNew"), "Command"));
        Assert.NotNull(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Classes\.zqxv"), "Content Type"));
        Assert.Null(RegistryGate.ValueBlock(new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zqxv\OpenWithProgids"), "Zqxv.Doc"));
        Assert.Null(RegistryGate.ValueBlock(new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"), "Zqxv"));
        Assert.NotNull(RegistryGate.ValueBlock(Lm(LeftoverScanner.SharedDllsPath), @"C:\x\a.dll"));
        Assert.NotNull(RegistryGate.ValueBlock(Lm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"), "Shell"));
    }
}
