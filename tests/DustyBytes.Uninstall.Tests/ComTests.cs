using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class ComTests
{
    const string Clsid = "{11111111-2222-3333-4444-555555555555}";
    const string OtherClsid = "{99999999-2222-3333-4444-555555555555}";
    const string TypeLib = "{AAAAAAAA-2222-3333-4444-555555555555}";
    const string Iid = "{BBBBBBBB-2222-3333-4444-555555555555}";
    const string AppId = "{CCCCCCCC-2222-3333-4444-555555555555}";
    const string Explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";

    [Fact]
    public void ComClassInInstallDirBringsItsLinkedEntries()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var dll = t.File(@"Programs\AcmeWidget\AcmeWidgetShell.dll");
        var reg = new FakeRegistryView();
        RegKeyRef Lm(string path, RegView view = RegView.Registry64) => reg.Key(RegHive.LocalMachine, view, path);
        var classes = Lm(@"SOFTWARE\Classes");
        reg.Set(classes.Child(@"CLSID\" + Clsid), "", "Acme Widget");
        reg.Set(classes.Child(@"CLSID\" + Clsid), "AppID", AppId);
        reg.Set(classes.Child($@"CLSID\{Clsid}\InprocServer32"), "", dll);
        reg.Set(classes.Child($@"CLSID\{Clsid}\TypeLib"), "", TypeLib);
        reg.Set(classes.Child($@"CLSID\{Clsid}\ProgID"), "", "AcmeWidget.Shell.1");
        reg.Set(classes.Child(@"AcmeWidget.Shell.1\CLSID"), "", Clsid);
        reg.Set(classes.Child(@"CLSID\" + OtherClsid + @"\InprocServer32"), "", @"C:\Windows\System32\shell32.dll");
        reg.Set(classes.Child($@"TypeLib\{TypeLib}\1.0\0\win64"), "", dll);
        reg.Set(classes.Child($@"Interface\{Iid}\TypeLib"), "", TypeLib);
        reg.Set(classes.Child(@"AppID\" + AppId), "", "Acme Widget");
        reg.Set(classes.Child(@"*\shellex\ContextMenuHandlers\AcmeWidget"), "", Clsid);
        reg.Set(classes.Child(@"*\shellex\ContextMenuHandlers\Other"), "", OtherClsid);
        reg.Set(Lm(Explorer + @"\ShellIconOverlayIdentifiers\ AcmeWidgetOk"), "", Clsid);
        var approved = Lm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
        reg.Set(approved, Clsid, "Acme Widget Shell");
        reg.Set(approved, OtherClsid, "Other");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        string[] expected =
        [
            @"CLSID\" + Clsid, @"TypeLib\" + TypeLib, @"Interface\" + Iid, @"AppID\" + AppId, "AcmeWidget.Shell.1",
            @"ContextMenuHandlers\AcmeWidget", @"ShellIconOverlayIdentifiers\ AcmeWidgetOk",
        ];
        foreach (var e in expected)
            Assert.Contains(snap.Candidates, c => c.Kind == LeftoverKind.RegistryKey && c.Target.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        var value = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.RegistryValue);
        Assert.Equal(Clsid, value.ValueName);
        Assert.DoesNotContain(snap.Candidates, c => c.Target.Contains(OtherClsid) || c.Target.EndsWith(@"\Other"));
        var com = Assert.Single(snap.Candidates, c => c.Target.EndsWith(@"CLSID\" + Clsid));
        Assert.Contains(com.Evidence, e => e.Code == "com-server");
        Assert.True(com.AutoRemovable);
    }

    [Fact]
    public void ComClassOutsideInstallDirIsNotTouched()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var reg = new FakeRegistryView();
        var classes = reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes");
        reg.Set(classes.Child(@"CLSID\" + Clsid), "", "Acme Widget Shell");
        reg.Set(classes.Child($@"CLSID\{Clsid}\InprocServer32"), "", @"C:\Program Files\Common Files\acme.dll");
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");

        var snap = new LeftoverScanner(Fixture.Context(reg, new FakeProbe(), [program], [bases])).Snapshot(program);

        Assert.DoesNotContain(snap.Candidates, c => c.Target.Contains(Clsid));
    }

    [Fact]
    public void GateAllowsOnlySingleComEntries()
    {
        var lm = (string p) => new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, p);
        Assert.Null(RegistryGate.KeyBlock(lm(@"SOFTWARE\Classes\Interface\" + Iid)));
        Assert.Null(RegistryGate.KeyBlock(lm(@"SOFTWARE\Classes\AppID\" + AppId)));
        Assert.NotNull(RegistryGate.KeyBlock(lm(@"SOFTWARE\Classes\AppID\acme.exe")));
        Assert.NotNull(RegistryGate.KeyBlock(lm($@"SOFTWARE\Classes\CLSID\{Clsid}\InprocServer32")));
        Assert.Null(RegistryGate.KeyBlock(lm(Explorer + @"\ShellIconOverlayIdentifiers\ AcmeWidgetOk")));
        Assert.NotNull(RegistryGate.KeyBlock(lm(Explorer + @"\ShellIconOverlayIdentifiers")));
    }
}
