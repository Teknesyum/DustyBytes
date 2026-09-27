using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class ValueLevelTests
{
    const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion\";

    sealed class Bench : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly FakeRegistryView Reg = new();
        public readonly string Bases;
        public readonly string Dir;
        public readonly string Exe;

        public Bench()
        {
            Bases = Tree.Dir("Programs");
            Dir = Tree.Dir(@"Programs\AcmeWidget");
            Exe = Tree.File(@"Programs\AcmeWidget\widget.exe");
        }

        public RegKeyRef Hkcu(string path) => Reg.Key(RegHive.CurrentUser, RegView.Registry64, path);

        public RegKeyRef Hklm(string path, RegView view = RegView.Registry64) => Reg.Key(RegHive.LocalMachine, view, path);

        public LeftoverSnapshot Scan(string? publisher = "Acme Inc.")
        {
            var program = Fixture.Program("Acme Widget", Dir, publisher);
            return new LeftoverScanner(Fixture.Context(Reg, new FakeProbe(), [program], [Bases])).Snapshot(program);
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void StartupApprovedValueFollowsItsRunEntry()
    {
        using var b = new Bench();
        b.Reg.Set(b.Hkcu(CurrentVersion + "Run"), "AcmeWidget", $"\"{b.Exe}\" /tray");
        var approved = b.Hkcu(CurrentVersion + @"Explorer\StartupApproved\Run");
        b.Reg.Set(approved, "AcmeWidget", new byte[] { 2, 0, 0, 0 });
        b.Reg.Set(approved, "OneDrive", new byte[] { 2, 0, 0, 0 });

        var snap = b.Scan();

        var value = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.RegistryValue && c.Key == approved);
        Assert.Equal("AcmeWidget", value.ValueName);
        Assert.Contains(value.Evidence, e => e.Code == "linked");
        Assert.True(value.AutoRemovable);
    }

    [Fact]
    public void AppPathsKeyPointingIntoInstallDirIsACandidate()
    {
        using var b = new Bench();
        b.Reg.Set(b.Hklm(CurrentVersion + @"App Paths\widget.exe"), "", b.Exe);
        b.Reg.Set(b.Hklm(CurrentVersion + @"App Paths\AcmeWidget.exe", RegView.Registry32), "Path", b.Dir);
        b.Reg.Set(b.Hklm(CurrentVersion + @"App Paths\notepad.exe"), "", @"C:\Windows\notepad.exe");

        var snap = b.Scan();

        var keys = snap.Candidates.Where(c => c.Kind == LeftoverKind.RegistryKey && c.Target.Contains("App Paths")).ToList();
        Assert.Equal(2, keys.Count);
        Assert.DoesNotContain(keys, c => c.Target.Contains("notepad"));
        Assert.Contains(keys, c => c.Tier == ConfidenceTier.High && c.AutoRemovable);
    }

    [Fact]
    public void AssociationValuesAreOfferedButNeverAutomatic()
    {
        using var b = new Bench();
        var classes = b.Hkcu(@"Software\Classes");
        var ext = classes.Child(".acw");
        b.Reg.Set(ext, "", "AcmeWidget.Document");
        b.Reg.Set(ext, "Content Type", "application/x-acw");
        b.Reg.Set(b.Hkcu(@"Software\Classes\.acw\OpenWithProgids"), "AcmeWidget.Document", "");
        b.Reg.Set(b.Hkcu(@"Software\Classes\AcmeWidget.Document\shell\open\command"), "", $"\"{b.Exe}\" \"%1\"");
        var fileExts = b.Hkcu(CurrentVersion + @"Explorer\FileExts\.acw\OpenWithProgids");
        b.Reg.Set(fileExts, "AcmeWidget.Document", "");

        var snap = b.Scan();

        var values = snap.Candidates.Where(c => c.Kind == LeftoverKind.RegistryValue).ToList();
        Assert.Equal(3, values.Count);
        var def = Assert.Single(values, c => c.Key == ext);
        Assert.Equal("", def.ValueName);
        Assert.Equal("AcmeWidget.Document", def.ExpectValue);
        Assert.Contains(values, c => c.Key!.Identity == fileExts.Identity);
        Assert.All(values, c => Assert.Equal(ConfidenceTier.Medium, c.Tier));
        Assert.All(values, c => Assert.False(c.AutoRemovable));
        Assert.DoesNotContain(values, c => c.ValueName == "Content Type");
        Assert.NotNull(RegistryGate.ValueBlock(ext, "Content Type"));
    }

    [Fact]
    public void ClassWithoutProgramNameIsCappedAtMedium()
    {
        using var b = new Bench();
        var classes = b.Hkcu(@"Software\Classes");
        b.Reg.Set(classes.Child(".qqz"), "", "Zork.File");
        b.Reg.Set(classes.Child(@"Zork.File\shell\open\command"), "", $"\"{b.Exe}\" \"%1\"");

        var snap = b.Scan();

        var assoc = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.FileAssociation);
        Assert.Contains(assoc.Evidence, e => e.Code == "class-no-name");
        Assert.NotEqual(ConfidenceTier.High, assoc.Tier);
    }

    [Fact]
    public void DriverServiceIsNeverAutomatic()
    {
        using var b = new Bench();
        var sys = b.Tree.File(@"Programs\AcmeWidget\acmeflt.sys");
        var svc = b.Hklm(LeftoverScanner.ServicesPath + @"\AcmeWidgetFilter");
        b.Reg.SetAll(svc, ("ImagePath", sys), ("DisplayName", "Acme Widget"), ("Type", 1));

        var snap = b.Scan();

        var service = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Service);
        Assert.Contains(service.Evidence, e => e.Code == "driver");
        Assert.Equal(ConfidenceTier.Medium, service.Tier);
        Assert.False(service.AutoRemovable);
    }
}
