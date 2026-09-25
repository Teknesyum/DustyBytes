using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class EnumerationTests
{
    static readonly EnumerateOptions Options = new() { IncludeMsix = false, MeasureSize = false, DetectBySignature = false };

    static List<InstalledProgram> Run(FakeRegistryView reg, EnumerateOptions? o = null) =>
        InstalledPrograms.EnumerateRegistry(reg, new FakeProbe(), o ?? Options);

    [Fact]
    public void SameKeyIn64And32ViewsIsListedOnce()
    {
        var reg = new FakeRegistryView();
        foreach (var view in new[] { RegView.Registry64, RegView.Registry32 })
            reg.SetAll(Fixture.UninstallKey(reg, "AcmeWidget", view: view), ("DisplayName", "Acme Widget"), ("DisplayVersion", "1.0"), ("UninstallString", @"C:\Acme\uninstall.exe"));

        var list = Run(reg);

        Assert.Single(list);
        Assert.Equal("reg:HKLM64:AcmeWidget", list[0].Id);
        Assert.True(list[0].Is64Bit);
    }

    [Fact]
    public void PerUserDuplicateAcrossViewsIsListedOnce()
    {
        var reg = new FakeRegistryView();
        foreach (var view in new[] { RegView.Registry64, RegView.Registry32 })
            reg.SetAll(Fixture.UninstallKey(reg, "Zed", RegHive.CurrentUser, view), ("DisplayName", "Zed Editor"), ("UninstallString", @"C:\Users\u\AppData\Local\Zed\unins000.exe"));

        var list = Run(reg);

        var p = Assert.Single(list);
        Assert.True(p.PerUser);
        Assert.StartsWith("reg:HKCU64:", p.Id);
    }

    [Fact]
    public void DifferentProgramsWithSameKeyNameInDifferentViewsAreKept()
    {
        var reg = new FakeRegistryView();
        reg.SetAll(Fixture.UninstallKey(reg, "Tool", view: RegView.Registry64), ("DisplayName", "Tool x64"), ("UninstallString", @"C:\a\u.exe"));
        reg.SetAll(Fixture.UninstallKey(reg, "Tool", view: RegView.Registry32), ("DisplayName", "Tool x86"), ("UninstallString", @"C:\b\u.exe"));

        Assert.Equal(2, Run(reg).Count);
    }

    [Fact]
    public void SystemComponentParentKeyAndUpdatesAreHidden()
    {
        var reg = new FakeRegistryView();
        reg.SetAll(Fixture.UninstallKey(reg, "Visible"), ("DisplayName", "Visible App"), ("UninstallString", @"C:\v\u.exe"));
        reg.SetAll(Fixture.UninstallKey(reg, "Sys"), ("DisplayName", "Hidden Component"), ("SystemComponent", 1));
        reg.SetAll(Fixture.UninstallKey(reg, "KB500"), ("DisplayName", "Patch for Visible"), ("ParentKeyName", "Visible"));
        reg.SetAll(Fixture.UninstallKey(reg, "KB600"), ("DisplayName", "Security Update for X"), ("ReleaseType", "Security Update"));
        reg.SetAll(Fixture.UninstallKey(reg, "NoName"), ("UninstallString", @"C:\n\u.exe"));

        var list = Run(reg);
        Assert.Equal(["Visible App"], list.Select(p => p.DisplayName));

        var all = Run(reg, Options with { IncludeHidden = true });
        Assert.Equal(4, all.Count);
        Assert.DoesNotContain(all, p => p.KeyName == "NoName");
    }

    [Fact]
    public void MsiProductCodeAndInstallLocationFromUserData()
    {
        const string code = "{90120000-0030-0000-0000-0000000FF1CE}";
        var reg = new FakeRegistryView();
        reg.SetAll(Fixture.UninstallKey(reg, code), ("DisplayName", "Office Test"), ("WindowsInstaller", 1), ("UninstallString", "MsiExec.exe /X" + code));
        var props = reg.Key(RegHive.LocalMachine, RegView.Registry64, InstalledPrograms.MsiUserDataProducts + @"\00002109030000000000000000F01FEC\InstallProperties");
        reg.Set(props, "InstallLocation", @"C:\Office Test\");
        reg.SetAll(Fixture.UninstallKey(reg, code, view: RegView.Registry32), ("DisplayName", "Office Test (32)"), ("WindowsInstaller", 1));

        var list = Run(reg);

        var p = Assert.Single(list);
        Assert.Equal(code, p.ProductCode);
        Assert.Equal(InstallerType.Msi, p.Installer);
        Assert.NotNull(p.InstallLocation);
        Assert.StartsWith(@"C:\Office Test", p.InstallLocation);
        Assert.Equal("msiexec.exe /x " + code + " /qb /norestart", Uninstaller.BuildCommand(p)!.CommandLine);
    }

    [Fact]
    public void BracedKeyWithoutMsiIsNotTreatedAsProductCode()
    {
        var reg = new FakeRegistryView();
        reg.SetAll(Fixture.UninstallKey(reg, "{11111111-2222-3333-4444-555555555555}"), ("DisplayName", "Guid Named"), ("UninstallString", @"C:\g\uninst.exe"));

        var p = Assert.Single(Run(reg));
        Assert.Null(p.ProductCode);
        Assert.Equal(InstallerType.Nsis, p.Installer);
    }

    [Fact]
    public void InstallDateAndSizeAreRead()
    {
        var reg = new FakeRegistryView();
        reg.SetAll(Fixture.UninstallKey(reg, "Dated"), ("DisplayName", "Dated App"), ("InstallDate", "20240115"), ("EstimatedSize", 2048), ("UninstallString", @"C:\d\u.exe"));

        var p = Assert.Single(Run(reg));
        Assert.Equal(new DateOnly(2024, 1, 15), p.InstallDate);
        Assert.Equal(2048L * 1024, p.EstimatedSizeBytes);
    }

    [Theory]
    [InlineData("Foo_is1", false, @"C:\Foo\whatever.exe", InstallerType.Inno)]
    [InlineData("Foo", false, @"""C:\Foo\unins000.exe""", InstallerType.Inno)]
    [InlineData("Foo", true, @"C:\Foo\x.exe", InstallerType.Msi)]
    [InlineData("Foo", false, @"MsiExec.exe /X{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", InstallerType.Msi)]
    [InlineData("Foo", false, @"""C:\Program Files (x86)\InstallShield Installation Information\{X}\setup.exe"" -runfromtemp", InstallerType.InstallShield)]
    [InlineData("Foo", false, @"C:\Foo\uninstall.exe", InstallerType.Nsis)]
    [InlineData("Foo", false, @"C:\Foo\remove.exe", InstallerType.Unknown)]
    public void InstallerTypeGuess(string keyName, bool msi, string uninstall, InstallerType expected) =>
        Assert.Equal(expected, InstallerDetector.Detect(keyName, msi, uninstall, null, null, null, null));

    [Fact]
    public void InstallerTypeFromFileHead()
    {
        var probe = new FakeProbe();
        probe.Heads[@"C:\Foo\remove.exe"] = [.. "MZ....Nullsoft Install System"u8];
        probe.Heads[@"C:\Bar\remove.exe"] = [.. "MZ....Inno Setup Setup Data"u8];

        Assert.Equal(InstallerType.Nsis, InstallerDetector.Detect("Foo", false, @"C:\Foo\remove.exe", null, null, null, probe));
        Assert.Equal(InstallerType.Inno, InstallerDetector.Detect("Bar", false, @"C:\Bar\remove.exe", null, null, null, probe));
    }

    [Fact]
    public void InnoAppPathValueMeansInno()
    {
        var reg = new FakeRegistryView();
        var key = Fixture.UninstallKey(reg, "Something");
        reg.Set(key, "Inno Setup: App Path", @"C:\S");
        Assert.Equal(InstallerType.Inno, InstallerDetector.Detect("Something", false, @"C:\S\x.exe", null, reg, key, null));
    }
}
