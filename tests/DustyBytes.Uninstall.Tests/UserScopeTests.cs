using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;

namespace DustyBytes.Uninstall.Tests;

public class UserScopeTests
{
    const string Sid = "S-1-5-21-111-222-333-1001";

    static FakeRegistryView LoadedUser(string profile)
    {
        var reg = new FakeRegistryView();
        reg.Key(RegHive.Users, RegView.Registry64, Sid);
        reg.Set(reg.Key(RegHive.LocalMachine, RegView.Registry64, UserScope.ProfileListPath + "\\" + Sid), "ProfileImagePath", profile);
        return reg;
    }

    [Fact]
    public void ResolvesProfileAndShellFoldersOfThePipeUser()
    {
        using var t = new TempTree();
        var profile = t.Dir(@"Users\ayse");
        var reg = LoadedUser(profile);
        var folders = reg.Key(RegHive.Users, RegView.Registry64, Sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        reg.Set(folders, "AppData", @"%USERPROFILE%\AppData\Roaming", RegKind.ExpandString);
        reg.Set(folders, "Programs", @"%APPDATA%\Microsoft\Windows\Start Menu\Programs", RegKind.ExpandString);
        reg.Set(reg.Key(RegHive.Users, RegView.Registry64, Sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"), "Programs", profile + @"\AppData\Roaming\Microsoft\Windows\Start Menu\Programs");

        var user = UserScope.Resolve(Sid, reg);

        Assert.Equal(Sid, user.Sid);
        Assert.Equal(profile, user.ProfilePath);
        Assert.Null(user.Note);
        Assert.Equal(profile + @"\AppData\Roaming", user.Folder(reg, "AppData"));
        Assert.Equal(profile + @"\AppData\Roaming\Microsoft\Windows\Start Menu\Programs", user.Folder(reg, "Programs"));

        var ctx = ScanContext.ForSystem(Fixture.EmptyProtection(), [], reg, new FakeProbe(), user);
        Assert.Contains(ctx.DataBases, d => d.Equals(Paths.Normalize(profile + @"\AppData\Roaming"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.ShortcutDirs, d => d.StartsWith(Paths.Normalize(profile), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new RegKeyRef(RegHive.Users, RegView.Registry64, Sid + @"\Software"), ctx.User("Software"));
        Assert.Equal(new RegKeyRef(RegHive.Users, RegView.Registry64, Sid + "_Classes"), ctx.UserClasses);
    }

    [Fact]
    public void UnknownOrUnloadedUserFallsBackToCurrentUserWithANote()
    {
        var reg = new FakeRegistryView();
        var none = UserScope.Resolve(null, reg);
        var unloaded = UserScope.Resolve(Sid, reg);

        Assert.Null(none.Sid);
        Assert.Null(unloaded.Sid);
        Assert.Contains("HKCU", unloaded.Note);
        var ctx = ScanContext.ForSystem(Fixture.EmptyProtection(), [], reg, new FakeProbe(), unloaded);
        Assert.Equal(RegHive.CurrentUser, ctx.User("Software").Hive);
        var program = Fixture.Program("Zqxv", null);
        Assert.Contains(new LeftoverScanner(ctx).Snapshot(program).Notes, n => n == unloaded.Note);
    }

    [Fact]
    public void PerUserProgramsComeFromThePipeUsersHiveWithStableIds()
    {
        var reg = LoadedUser(@"C:\Users\ayse");
        var mine = reg.Key(RegHive.Users, RegView.Registry64, $@"{Sid}\{InstalledPrograms.UninstallPath}\Zqxv");
        reg.SetAll(mine, ("DisplayName", "Zqxv"), ("UninstallString", @"C:\Users\ayse\AppData\Local\Zqxv\uninstall.exe"));
        var admin = reg.Key(RegHive.CurrentUser, RegView.Registry64, $@"{InstalledPrograms.UninstallPath}\AdminOnly");
        reg.SetAll(admin, ("DisplayName", "AdminOnly"), ("UninstallString", @"C:\Users\admin\x.exe"));

        var list = InstalledPrograms.EnumerateRegistry(reg, null, new EnumerateOptions { MeasureSize = false, DetectBySignature = false, UserSid = Sid });

        var p = Assert.Single(list);
        Assert.Equal("reg:HKCU64:Zqxv", p.Id);
        Assert.True(p.PerUser);
        Assert.Equal(RegHive.Users, p.Key!.Hive);
    }

    [Fact]
    public void ScannerLooksInThePipeUsersHive()
    {
        using var t = new TempTree();
        var bases = t.Dir("Programs");
        var dir = t.Dir(@"Programs\AcmeWidget");
        var exe = t.File(@"Programs\AcmeWidget\widget.exe");
        var reg = LoadedUser(t.Root);
        var soft = reg.Key(RegHive.Users, RegView.Registry64, Sid + @"\Software\Acme Widget");
        reg.Set(soft, "Path", dir);
        reg.Set(reg.Key(RegHive.Users, RegView.Registry64, Sid + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), "AcmeWidget", $"\"{exe}\"");
        reg.Set(reg.Key(RegHive.CurrentUser, RegView.Registry64, @"Software\Acme Widget"), "Path", dir);
        var program = Fixture.Program("Acme Widget", dir, "Acme Inc.");
        var ctx = Fixture.Context(reg, new FakeProbe(), [program], [bases]);
        ctx = new ScanContext
        {
            Registry = reg,
            Probe = ctx.Probe,
            Protection = ctx.Protection,
            Programs = ctx.Programs,
            BroadRoots = ctx.BroadRoots,
            DataBases = ctx.DataBases,
            UserSid = Sid,
        };

        var snap = new LeftoverScanner(ctx).Snapshot(program);

        var key = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.RegistryKey && c.Target.Contains("Acme Widget"));
        Assert.Equal(RegHive.Users, key.Key!.Hive);
        Assert.True(key.IsSettings);
        Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.StartupEntry && c.Key!.Hive == RegHive.Users);
        Assert.DoesNotContain(snap.Candidates, c => c.Key?.Hive == RegHive.CurrentUser);
    }
}
