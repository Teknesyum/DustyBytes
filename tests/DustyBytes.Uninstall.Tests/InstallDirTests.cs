using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;

namespace DustyBytes.Uninstall.Tests;

public class InstallDirTests
{
    const string Code = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    static LeftoverCandidate? InstallFolder(LeftoverSnapshot snap) =>
        snap.Candidates.FirstOrDefault(c => c.Kind == LeftoverKind.Folder && c.Evidence.Any(e => e.Code is "install-dir" or "install-dir-inferred"));

    [Fact]
    public void InnoAppPathFillsAnEmptyInstallLocation()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\Zqxv");
        var reg = new FakeRegistryView();
        var key = Fixture.UninstallKey(reg, "Zqxv_is1");
        reg.Set(key, "Inno Setup: App Path", dir);
        var program = Fixture.Program("Zqxv", null, null, key);

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        var folder = InstallFolder(snap);
        Assert.NotNull(folder);
        Assert.Equal(Paths.Normalize(dir), folder.Target, ignoreCase: true);
        Assert.Contains(snap.Notes, n => n.Contains("Inno Setup"));
    }

    [Fact]
    public void MsiInstallPropertiesFillAnEmptyInstallLocation()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\Zqxv");
        var reg = new FakeRegistryView();
        Assert.True(MsiGuid.TryParseBraced(Code, out var g));
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, InstalledPrograms.MsiUserDataProducts + "\\" + MsiGuid.Compress(g) + @"\InstallProperties"), "InstallLocation", dir + "\\");
        var program = Fixture.Program("Zqxv", null) with { ProductCode = Code, WindowsInstaller = true };

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        Assert.Equal(Paths.Normalize(dir), InstallFolder(snap)?.Target, ignoreCase: true);
        Assert.Contains(snap.Notes, n => n.Contains("MSI"));
    }

    [Fact]
    public void AppPathsEntryWithTheProgramNameIsUsed()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\Zqxv");
        var exe = t.File(@"Programs\Zqxv\zqxv.exe");
        var reg = new FakeRegistryView();
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\zqxv.exe"), "", exe);
        var program = Fixture.Program("Zqxv", null);

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        var folder = InstallFolder(snap);
        Assert.NotNull(folder);
        Assert.Equal(Paths.Normalize(dir), folder.Target, ignoreCase: true);
        Assert.Contains(folder.Evidence, e => e.Code == "install-dir-inferred");
    }

    [Fact]
    public void AppPathsOutsideKnownBasesOrUnderAnotherFolderNameIsIgnored()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var elsewhere = t.File(@"Elsewhere\Zqxv\zqxv.exe");
        var other = t.File(@"Programs\Suite\zqxv.exe");
        var reg = new FakeRegistryView();
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\zqxv.exe"), "", elsewhere);
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\zqxv.exe"), "", other);
        var program = Fixture.Program("Zqxv", null);

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        Assert.Null(InstallFolder(snap));
        Assert.Contains(snap.Notes, n => n.Contains("Kurulum klasörü bulunamadı"));
    }
}
