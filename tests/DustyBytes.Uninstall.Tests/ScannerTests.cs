using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;

namespace DustyBytes.Uninstall.Tests;

public class ScannerTests
{
    static bool Under(string path, string root) => Paths.IsUnder(path, root);

    [Fact]
    public void InstallFolderAndUninstallKeyAreHighAndChecked()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        t.File(@"Programs\AcmeWidget\widget.exe");
        var reg = new FakeRegistryView();
        var key = Fixture.UninstallKey(reg, "AcmeWidget");
        reg.Set(key, "DisplayName", "Acme Widget");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.", key);
        var scanner = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        var folder = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        Assert.Equal(Paths.Normalize(dir), folder.Target, ignoreCase: true);
        Assert.Equal(ConfidenceTier.High, folder.Tier);
        Assert.True(folder.Checked);
        Assert.False(string.IsNullOrWhiteSpace(folder.Reason));
        var uk = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.RegistryKey && c.Key == key);
        Assert.Equal(ConfidenceTier.High, uk.Tier);
        Assert.All(snap.Candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.Reason)));
    }

    [Fact]
    public void GenericNamedFoldersAreNotCandidates()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        t.Dir(@"Programs\Tools");
        t.Dir(@"Programs\Common");
        t.Dir(@"Programs\Update");
        var program = Fixture.Program("Acme Widget Tools", null, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.Empty(snap.Candidates);
    }

    [Fact]
    public void NameOnlyDataFolderIsLowAndUnchecked()
    {
        using var t = new TempTree();
        var bases = t.Dir("AppData");
        t.File(@"AppData\AcmeWidget\settings.json", "{}");
        var program = Fixture.Program("Acme Widget", null, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases]));

        var c = Assert.Single(scanner.Snapshot(program).Candidates);
        Assert.Equal(ConfidenceTier.Low, c.Tier);
        Assert.False(c.Checked);
        Assert.Equal(1, c.Anchors);
    }

    [Fact]
    public void PortableLookingDataFolderIsNeverACandidate()
    {
        using var t = new TempTree();
        var bases = t.Dir("AppData");
        var dll = t.File(@"AppData\AcmeWidget\helper.exe");
        var probe = new FakeProbe();
        probe.Identities[dll] = new FileIdentity("Acme Inc.", "Acme Widget", null, false);
        var installed = t.Dir(@"Other\AcmeWidget");
        var program = Fixture.Program("Acme Widget", installed, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), probe, [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => Under(c.Target, bases));
        Assert.Contains(snap.Blocked, b => Under(b.Target, bases) && b.Reason.Contains("Taşınabilir"));
    }

    [Fact]
    public void FolderUnderAnotherProgramsInstallLocationIsDropped()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var host = t.Dir(@"Programs\HostSuite");
        var plugin = t.Dir(@"Programs\HostSuite\Plugins\AcmeWidget");
        t.File(@"Programs\HostSuite\Plugins\AcmeWidget\widget.dll");
        var other = Fixture.Program("Host Suite", host, "Host Corp") with { Id = "reg:HKLM64:Host" };
        var program = Fixture.Program("Acme Widget", plugin, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [other, program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        Assert.Contains(snap.Blocked, b => b.Target.Equals(Paths.Normalize(plugin), StringComparison.OrdinalIgnoreCase) && b.Reason.Contains("Host Suite"));
        Assert.DoesNotContain(snap.Candidates, c => Under(host, c.Target) || Under(c.Target, host));
    }

    [Fact]
    public void DownloadsProgramFolderIsNeverACandidate()
    {
        using var t = new TempTree();
        var downloads = t.Dir("Downloads");
        var dir = t.Dir(@"Downloads\AcmeWidget");
        t.File(@"Downloads\AcmeWidget\widget.exe");
        var reg = new FakeRegistryView();
        var run = reg.Key(RegHive.CurrentUser, RegView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        reg.Set(run, "AcmeWidget", $"\"{dir}\\widget.exe\" /tray");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [downloads], userData: [downloads]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut);
        Assert.Contains(snap.Blocked, b => b.Target.Equals(Paths.Normalize(dir), StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(scanner.GateFolder(downloads));
        Assert.NotNull(scanner.GateFolder(Path.Combine(dir, "sub")));
    }

    [Fact]
    public void NeverLeftoverRootFromProtectedListIsNeverACandidate()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var desktop = t.Dir("Desktop");
        var dir = t.Dir(@"Desktop\AcmeWidget");
        t.File(@"Desktop\AcmeWidget\widget.exe");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");
        var ctx = Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases], protection: Fixture.EmptyProtection(desktop));
        var scanner = new LeftoverScanner(ctx);

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => Under(c.Target, desktop));
        Assert.NotNull(scanner.GateFolder(dir));
    }

    [Fact]
    public void ProtectedListRejectionBlocksFolder()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var protection = Fixture.EmptyProtection();
        protection.AddUserException(dir);
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases], protection: protection));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        Assert.Contains(snap.Blocked, b => b.Reason.StartsWith("Korumalı liste"));
    }

    [Fact]
    public void BroadInstallLocationIsIgnored()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var program = Fixture.Program("Acme Widget", bases, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        Assert.Contains(snap.Notes, n => n.Contains("çok geniş"));
    }

    [Fact]
    public void SharedPublisherOnlyExactProductSubkey()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        t.Dir(@"Programs\NVIDIA Corporation\PhysX");
        var reg = new FakeRegistryView();
        var vendor = reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\NVIDIA Corporation");
        reg.Set(vendor.Child("PhysX"), "Version", "9");
        reg.Set(vendor.Child("Global"), "x", "1");
        reg.Set(vendor.Child("NVIDIA PhysX System Software"), "Version", "9");
        var program = Fixture.Program("NVIDIA PhysX System Software", null, "NVIDIA Corporation");
        var scanner = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Key == vendor);
        Assert.DoesNotContain(snap.Candidates, c => c.Key == vendor.Child("PhysX"));
        Assert.DoesNotContain(snap.Candidates, c => c.Key == vendor.Child("Global"));
        var product = Assert.Single(snap.Candidates, c => c.Key == vendor.Child("NVIDIA PhysX System Software"));
        Assert.Equal(ConfidenceTier.Medium, product.Tier);
        Assert.False(product.Checked);
        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
    }

    [Fact]
    public void PublisherFolderItselfIsBlockedButProductSubfolderIsKept()
    {
        using var t = new TempTree();
        var bases = t.Dir("AppData");
        t.File(@"AppData\Acme\Widget\state.json", "{}");
        t.File(@"AppData\Acme\OtherProduct\state.json", "{}");
        var program = Fixture.Program("Acme Widget", null, "Acme");
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Target.Equals(Paths.Normalize(Path.Combine(bases, "Acme")), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snap.Candidates, c => c.Target.EndsWith("OtherProduct", StringComparison.OrdinalIgnoreCase));
        var widget = Assert.Single(snap.Candidates);
        Assert.EndsWith(@"Acme\Widget", widget.Target, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConfidenceTier.Low, widget.Tier);
    }

    [Fact]
    public void RegistryDetectsServiceRunFirewallAndAssociation()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var exe = t.File(@"Programs\AcmeWidget\widget.exe");
        var reg = new FakeRegistryView();
        reg.SetAll(reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.ServicesPath + @"\AcmeWidgetSvc"), ("ImagePath", $"\"{exe}\" --service"), ("DisplayName", "Acme Widget Service"));
        reg.SetAll(reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.ServicesPath + @"\Spooler"), ("ImagePath", @"C:\Windows\System32\spoolsv.exe"));
        reg.Set(reg.Key(RegHive.CurrentUser, RegView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "AcmeWidget", $"\"{exe}\" /tray");
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.FirewallRulesPath), "{RULE-1}", $"v2.30|Action=Allow|Active=TRUE|Dir=In|App={exe}|Name=Acme Widget|");
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.FirewallRulesPath), "{RULE-2}", @"v2.30|Action=Allow|App=C:\Other\o.exe|Name=Other|");
        var classes = reg.Key(RegHive.CurrentUser, RegView.Registry64, @"Software\Classes");
        reg.Set(classes.Child(".acw"), "", "AcmeWidget.Document");
        reg.Set(classes.Child(@"AcmeWidget.Document\shell\open\command"), "", $"\"{exe}\" \"%1\"");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");
        var scanner = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases]));

        var snap = scanner.Snapshot(program);

        Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Service && c.Target == "AcmeWidgetSvc");
        Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.StartupEntry && c.ValueName == "AcmeWidget");
        Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.FirewallRule && c.ValueName == "{RULE-1}");
        Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.FileAssociation);
        Assert.DoesNotContain(snap.Candidates, c => c.Target == "Spooler");
    }

    [Fact]
    public void StoreAppGetsNoLeftoverSearch()
    {
        var program = new InstalledProgram { Id = "msix:Acme_1.0_x64__abc", DisplayName = "Acme", Source = ProgramSource.Msix, PackageFullName = "Acme_1.0_x64__abc" };
        var scanner = new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [program], []));

        var snap = scanner.Snapshot(program);

        Assert.Empty(snap.Candidates);
        Assert.NotEmpty(snap.Notes);
    }

    [Fact]
    public void DiffIsEmptyWhileProgramStillInstalled()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var unins = t.File(@"Programs\AcmeWidget\unins000.exe");
        t.File(@"Programs\AcmeWidget\widget.exe");
        var reg = new FakeRegistryView();
        var key = Fixture.UninstallKey(reg, "AcmeWidget_is1");
        reg.SetAll(key, ("DisplayName", "Acme Widget"), ("UninstallString", $"\"{unins}\""));
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.", key, $"\"{unins}\"");
        var scanner = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases]));
        var before = scanner.Snapshot(program);
        Assert.NotEmpty(before.Candidates);

        var still = scanner.Diff(before);
        Assert.True(still.IsDiff);
        Assert.True(still.ProgramStillInstalled);
        Assert.Empty(still.Candidates);

        reg.DeleteKeyTree(key);
        File.Delete(unins);
        var after = scanner.Diff(before);
        Assert.False(after.ProgramStillInstalled);
        Assert.DoesNotContain(after.Candidates, c => c.Key == key);
        Assert.Single(after.Candidates, c => c.Kind == LeftoverKind.Folder);
    }

    [Fact]
    public void FirewallRuleParsing()
    {
        var d = LeftoverScanner.ParseFirewallRule(@"v2.30|Action=Allow|Active=TRUE|Dir=In|App=%ProgramFiles%\Acme\a.exe|Name=Acme|Desc=x=y|");
        Assert.Equal(@"%ProgramFiles%\Acme\a.exe", d["App"]);
        Assert.Equal("Acme", d["Name"]);
        Assert.Equal("x=y", d["Desc"]);
    }
}
