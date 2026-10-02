using System.Text.Json;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Uninstall.Tests;

[Collection("DryRunEnv")]
public class QuietUninstallTests : IDisposable
{
    readonly string? _saved = Environment.GetEnvironmentVariable(DryRun.Variable);

    public QuietUninstallTests() => Environment.SetEnvironmentVariable(DryRun.Variable, null);

    public void Dispose() => Environment.SetEnvironmentVariable(DryRun.Variable, _saved);

    const string Code = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    static InstalledProgram P(InstallerType type, string uninstall, string? quiet = null) => new()
    {
        Id = "reg:HKLM64:x",
        DisplayName = "X",
        Source = ProgramSource.Registry,
        UninstallString = uninstall,
        QuietUninstallString = quiet,
        Installer = type,
    };

    [Theory]
    [InlineData("Foo_is1", false, @"""C:\Foo\unins000.exe""", InstallerType.Inno)]
    [InlineData("Foo", false, @"""C:\Foo\unins001.exe"" /SILENT", InstallerType.Inno)]
    [InlineData("Foo", false, @"""C:\Foo\uninstall.exe""", InstallerType.Nsis)]
    [InlineData("Foo", false, @"C:\Foo\Uninst.exe -u", InstallerType.Nsis)]
    [InlineData("Foo", false, @"""C:\Foo\Uninstall-Foo.exe""", InstallerType.Nsis)]
    [InlineData("Foo", false, @"""C:\Users\u\AppData\Local\Foo\Update.exe"" --uninstall", InstallerType.Squirrel)]
    [InlineData(Code, true, @"MsiExec.exe /X" + Code, InstallerType.Msi)]
    [InlineData("Foo", false, @"MsiExec.exe /I" + Code, InstallerType.Msi)]
    [InlineData("Foo", false, @"""C:\Program Files (x86)\InstallShield Installation Information\" + Code + @"\setup.exe"" -runfromtemp -l0x0409 -removeonly", InstallerType.InstallShield)]
    [InlineData("Foo", false, @"C:\Foo\setup.exe -runfromtemp", InstallerType.InstallShield)]
    [InlineData("Foo", false, @"C:\Foo\remove.exe", InstallerType.Unknown)]
    public void InstallerTypeFollowsCommandHints(string keyName, bool msi, string uninstall, InstallerType expected) =>
        Assert.Equal(expected, InstallerDetector.Detect(keyName, msi, uninstall, null, null, null, null));

    [Fact]
    public void RegistryValuesRevealInstaller()
    {
        var reg = new FakeRegistryView();
        var inno = Fixture.UninstallKey(reg, "Plain");
        reg.Set(inno, "Inno Setup: App Path", @"C:\Foo");
        Assert.Equal(InstallerType.Inno, InstallerDetector.Detect("Plain", false, @"C:\Foo\remove.exe", null, reg, inno, null));

        var nsis = Fixture.UninstallKey(reg, "Other");
        reg.Set(nsis, "NSIS:Language", "1033");
        Assert.Equal(InstallerType.Nsis, InstallerDetector.Detect("Other", false, @"C:\Foo\remove.exe", null, reg, nsis, null));

        var none = Fixture.UninstallKey(reg, "None");
        reg.Set(none, "DisplayName", "None");
        Assert.Equal(InstallerType.Unknown, InstallerDetector.Detect("None", false, @"C:\Foo\remove.exe", null, reg, none, null));
    }

    [Fact]
    public void FileVersionTextRevealsInstaller()
    {
        var probe = new FakeProbe();
        probe.Heads[@"C:\Foo\a.exe"] = [1, 2, 3];
        probe.Versions[@"C:\Foo\a.exe"] = "Foo Ltd This installation was built with Inno Setup.";
        probe.Heads[@"C:\Foo\b.exe"] = [1, 2, 3];
        probe.Versions[@"C:\Foo\b.exe"] = "Nullsoft Install System v3.08";
        probe.Heads[@"C:\Foo\c.exe"] = [1, 2, 3];
        probe.Versions[@"C:\Foo\c.exe"] = "Foo Ltd InstallShield (R)";
        probe.Heads[@"C:\Foo\d.exe"] = [1, 2, 3];
        probe.Versions[@"C:\Foo\d.exe"] = "Foo Ltd Generic";

        Assert.Equal(InstallerType.Inno, InstallerDetector.Detect("K", false, @"C:\Foo\a.exe", null, null, null, probe));
        Assert.Equal(InstallerType.Nsis, InstallerDetector.Detect("K", false, @"C:\Foo\b.exe", null, null, null, probe));
        Assert.Equal(InstallerType.InstallShield, InstallerDetector.Detect("K", false, @"C:\Foo\c.exe", null, null, null, probe));
        Assert.Equal(InstallerType.Unknown, InstallerDetector.Detect("K", false, @"C:\Foo\d.exe", null, null, null, probe));
    }

    [Theory]
    [InlineData(InstallerType.Inno, @"""C:\X\unins000.exe""", @"""C:\X\unins000.exe"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART")]
    [InlineData(InstallerType.Nsis, @"""C:\X\uninstall.exe""", @"""C:\X\uninstall.exe"" /S")]
    [InlineData(InstallerType.Squirrel, @"C:\U\X\Update.exe --uninstall", @"C:\U\X\Update.exe --uninstall -s")]
    public void SilentKeysAreNotDuplicated(InstallerType type, string uninstall, string expected)
    {
        Assert.Equal(expected, Uninstaller.SilentCommand(P(type, uninstall))!.CommandLine);
        Assert.Equal(expected, Uninstaller.SilentCommand(P(type, expected))!.CommandLine);
    }

    [Fact]
    public void MsiGetsQuietKeysFromProductCode()
    {
        var p = P(InstallerType.Msi, "MsiExec.exe /I" + Code) with { WindowsInstaller = true, ProductCode = Code };
        var silent = Uninstaller.SilentCommand(p)!;
        Assert.True(silent.Silent);
        Assert.Equal($"msiexec.exe /x {Code} /qn /norestart REBOOT=ReallySuppress", silent.CommandLine);
        Assert.Equal($"msiexec.exe /x {Code}", Uninstaller.VisibleCommand(p)!.CommandLine);
    }

    [Fact]
    public void UnrecognizedTypesGetNoSilentKeys()
    {
        Assert.Null(Uninstaller.SilentCommand(P(InstallerType.Unknown, @"C:\X\remove.exe")));
        Assert.Null(Uninstaller.SilentCommand(P(InstallerType.InstallShield, @"C:\X\setup.exe -runfromtemp -removeonly")));
        Assert.Equal(@"C:\X\remove.exe", Uninstaller.VisibleCommand(P(InstallerType.Unknown, @"C:\X\remove.exe"))!.CommandLine);
    }

    [Fact]
    public void PlanSaysWhichProgramsOpenAWindow()
    {
        var inno = Uninstaller.Plan(P(InstallerType.Inno, @"""C:\X\unins000.exe"""));
        Assert.Equal(UninstallMode.Silent, inno.Mode);
        Assert.Equal(UninstallPlan.SilentBadge, inno.Badge);

        var vendorQuiet = Uninstaller.Plan(P(InstallerType.Unknown, "u.exe", "u.exe /quiet"));
        Assert.True(vendorQuiet.IsSilent);

        var unknown = Uninstaller.Plan(P(InstallerType.Unknown, @"C:\X\remove.exe"));
        Assert.Equal(UninstallMode.Visible, unknown.Mode);
        Assert.Equal(UninstallPlan.VisibleBadge, unknown.Badge);

        var shield = Uninstaller.Plan(P(InstallerType.InstallShield, @"C:\X\setup.exe -runfromtemp"));
        Assert.True(shield.OpensWindow);

        var msix = Uninstaller.Plan(new InstalledProgram { Id = "m", DisplayName = "M", Source = ProgramSource.Msix, PackageFullName = "M_1.0_x64__abc" });
        Assert.True(msix.IsSilent);

        Assert.Equal(UninstallMode.None, Uninstaller.Plan(P(InstallerType.Inno, "u.exe") with { NoRemove = true }).Mode);
        Assert.Equal(UninstallMode.None, Uninstaller.Plan(P(InstallerType.Inno, "u.exe") with { UninstallerMissing = true }).Mode);
        Assert.Equal(UninstallMode.None, Uninstaller.Plan(new InstalledProgram { Id = "n", DisplayName = "N" }).Mode);
        Assert.Equal("", Uninstaller.Plan(new InstalledProgram { Id = "n", DisplayName = "N" }).Badge);
    }

    [Theory]
    [InlineData(InstallerType.Msi, 0, true, false)]
    [InlineData(InstallerType.Msi, 3010, true, true)]
    [InlineData(InstallerType.Msi, 1641, true, true)]
    [InlineData(InstallerType.Msi, 1605, true, false)]
    [InlineData(InstallerType.Msi, 1614, true, false)]
    [InlineData(InstallerType.Msi, 1602, false, false)]
    [InlineData(InstallerType.Msi, 1618, false, false)]
    [InlineData(InstallerType.Msi, 1603, false, false)]
    [InlineData(InstallerType.Msi, 1625, false, false)]
    [InlineData(InstallerType.Inno, 0, true, false)]
    [InlineData(InstallerType.Inno, 2, false, false)]
    [InlineData(InstallerType.Nsis, 0, true, false)]
    [InlineData(InstallerType.Nsis, 1, false, false)]
    [InlineData(InstallerType.Nsis, 2, false, false)]
    [InlineData(InstallerType.Squirrel, 0, true, false)]
    [InlineData(InstallerType.Squirrel, 1, false, false)]
    [InlineData(InstallerType.Unknown, 3010, false, false)]
    public void ExitCodesAreInterpretedByInstallerType(InstallerType type, int code, bool ok, bool reboot)
    {
        var r = Uninstaller.Interpret(P(type, "u.exe"), code);
        Assert.Equal(ok, r.Ok);
        Assert.Equal(reboot, r.Reboot);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
    }

    [Fact]
    public void ExitCodeMessagesNameTheCode()
    {
        Assert.Contains("1603", Uninstaller.Interpret(P(InstallerType.Msi, "u.exe"), 1603).Message);
        Assert.Contains("77", Uninstaller.Interpret(P(InstallerType.Squirrel, "u.exe"), 77).Message);
        Assert.Contains(Uninstaller.RebootNote, Uninstaller.Interpret(P(InstallerType.Msi, "u.exe"), 3010).Message);
    }

    sealed class Bench : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly FakeRegistryView Reg = new();
        public readonly RegKeyRef Key;
        public readonly string Unins;
        public readonly string Dir;
        public readonly InstalledProgram Program;
        public readonly List<RunRequest> Runs = [];

        public Bench(InstallerType type, string exeName)
        {
            Dir = Tree.Dir("Zqxv");
            Unins = Tree.File(@"Zqxv\" + exeName);
            Key = Fixture.UninstallKey(Reg, "Zqxv");
            Reg.SetAll(Key, ("DisplayName", "Zqxv"), ("UninstallString", $"\"{Unins}\""));
            Program = Fixture.Program("Zqxv", Dir, null, Key, $"\"{Unins}\"") with { Installer = type, KeyName = "Zqxv" };
        }

        public void Remove()
        {
            Reg.DeleteKeyTree(Key);
            File.Delete(Unins);
        }

        public Uninstaller Make(Func<RunRequest, int, ProcessRunResult> run) => new(new LeftoverScanner(Fixture.Context(Reg, new FakeProbe(), [Program], [])))
        {
            LogDir = null,
            Runner = (req, ct) =>
            {
                Runs.Add(req);
                return Task.FromResult(run(req, Runs.Count));
            },
        };

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public async Task SilentOnlyNeverRunsAnUnrecognizedUninstaller()
    {
        using var b = new Bench(InstallerType.Unknown, "remove.exe");
        var u = b.Make((_, _) => new ProcessRunResult(true, true, 0, "ok"));

        var r = await u.RunVendorUninstaller(b.Program, null, default, VendorRunMode.SilentOnly);

        Assert.Empty(b.Runs);
        Assert.False(r.Ok);
        Assert.False(r.Ran);
        Assert.True(r.NeedsVisible);
    }

    [Fact]
    public async Task AutoModeRunsUnrecognizedUninstallerVisibleWithoutSilentAttempt()
    {
        using var b = new Bench(InstallerType.Unknown, "remove.exe");
        var u = b.Make((_, _) =>
        {
            b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program);

        Assert.True(r.Ok, r.Message);
        var run = Assert.Single(b.Runs);
        Assert.Equal($"\"{b.Unins}\"", run.CommandLine);
        Assert.Null(run.Timeout);
    }

    [Fact]
    public async Task SilentOnlyUsesPatienceAndDoesNotFallBackToVisibleOnTimeout()
    {
        using var b = new Bench(InstallerType.Inno, "unins000.exe");
        var u = b.Make((_, _) => new ProcessRunResult(true, false, -1, "süre doldu", TimedOut: true));

        var r = await u.RunVendorUninstaller(b.Program, null, default, VendorRunMode.SilentOnly, TimeSpan.FromSeconds(45));

        var run = Assert.Single(b.Runs);
        Assert.Contains("/VERYSILENT", run.CommandLine);
        Assert.Equal(TimeSpan.FromSeconds(45), run.Timeout);
        Assert.False(r.Ok);
        Assert.True(r.TimedOut);
        Assert.True(r.NeedsVisible);
    }

    [Fact]
    public async Task SilentOnlyFailureOffersVisibleButBusyInstallerDoesNot()
    {
        using var b = new Bench(InstallerType.Nsis, "uninstall.exe");
        var failed = await b.Make((_, _) => new ProcessRunResult(true, true, 2, "bitti")).RunVendorUninstaller(b.Program, null, default, VendorRunMode.SilentOnly);
        Assert.False(failed.Ok);
        Assert.True(failed.NeedsVisible);
        Assert.Single(b.Runs);

        using var m = new Bench(InstallerType.Msi, "msi.exe");
        var program = m.Program with { ProductCode = Code, UninstallString = $"\"{m.Unins}\" msiexec" };
        var busy = await m.Make((_, _) => new ProcessRunResult(true, true, 1618, "bitti")).RunVendorUninstaller(program, null, default, VendorRunMode.SilentOnly);
        Assert.False(busy.Ok);
        Assert.False(busy.NeedsVisible);
        Assert.Contains("Başka bir kurulum", busy.Message);
    }

    [Fact]
    public async Task SilentOnlySuccessIsDoneWithoutWindow()
    {
        using var b = new Bench(InstallerType.Nsis, "uninstall.exe");
        var u = b.Make((_, _) =>
        {
            b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program, null, default, VendorRunMode.SilentOnly);

        Assert.True(r.Ok);
        Assert.False(r.NeedsVisible);
        Assert.EndsWith("/S", Assert.Single(b.Runs).CommandLine);
    }

    [Fact]
    public async Task VisibleOnlySkipsTheSilentAttempt()
    {
        using var b = new Bench(InstallerType.Inno, "unins000.exe");
        var u = b.Make((_, _) =>
        {
            b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program, null, default, VendorRunMode.VisibleOnly);

        Assert.True(r.Ok, r.Message);
        var run = Assert.Single(b.Runs);
        Assert.Equal($"\"{b.Unins}\"", run.CommandLine);
        Assert.DoesNotContain("SILENT", run.CommandLine);
        Assert.Null(run.Timeout);
    }

    [Fact]
    public void HandlerFlagsAreParsedAndClamped()
    {
        Assert.Equal(VendorRunMode.Auto, UninstallHandlers.ModeOf([UninstallHandlers.AutoClean]));
        Assert.Equal(VendorRunMode.SilentOnly, UninstallHandlers.ModeOf([UninstallHandlers.SilentOnly]));
        Assert.Equal(VendorRunMode.VisibleOnly, UninstallHandlers.ModeOf([UninstallHandlers.VisibleOnly]));
        Assert.Null(UninstallHandlers.PatienceOf([UninstallHandlers.AutoClean]));
        Assert.Equal(TimeSpan.FromSeconds(300), UninstallHandlers.PatienceOf([UninstallHandlers.Patience(TimeSpan.FromMinutes(5))]));
        Assert.Equal(TimeSpan.FromSeconds(UninstallHandlers.MinPatienceSeconds), UninstallHandlers.PatienceOf([UninstallHandlers.SilentPatiencePrefix + "0"]));
        Assert.Equal(TimeSpan.FromSeconds(UninstallHandlers.MaxPatienceSeconds), UninstallHandlers.PatienceOf([UninstallHandlers.SilentPatiencePrefix + "999999"]));
        Assert.Null(UninstallHandlers.PatienceOf([UninstallHandlers.SilentPatiencePrefix + "abc"]));
    }

    [Fact]
    public async Task WorkerReportsSilentGiveUpAndRunsVisibleOnRequest()
    {
        using var b = new Bench(InstallerType.Inno, "unins000.exe");
        var store = new SnapshotStore(Path.Combine(b.Tree.Root, "store"));
        var visibleRuns = new List<string>();
        var h = new UninstallHandlers(Fixture.EmptyProtection(), _ => Task.FromResult(true), b.Reg, new FakeProbe(), store, new FakeSystemActions())
        {
            ProgramProvider = () => [b.Program],
            UninstallerFactory = scanner => new Uninstaller(scanner)
            {
                LogDir = null,
                Runner = (req, _) =>
                {
                    if (req.CommandLine.Contains("VERYSILENT"))
                        return Task.FromResult(new ProcessRunResult(true, false, -1, "süre doldu", TimedOut: true));
                    visibleRuns.Add(req.CommandLine);
                    b.Remove();
                    return Task.FromResult(new ProcessRunResult(true, true, 0, "ok"));
                },
            },
        };
        var progress = new Progress<WorkerProgress>();

        var first = await h.HandleUninstall(new WorkerRequest
        {
            Op = Ops.Uninstall,
            Target = b.Program.Id,
            UserApproved = true,
            Items = [UninstallHandlers.SkipRestorePoint, UninstallHandlers.AutoClean, UninstallHandlers.SilentOnly, UninstallHandlers.Patience(TimeSpan.FromSeconds(30))],
        }, progress, default);

        Assert.False(first.Ok);
        Assert.Contains(first.Items, i => i.Path == UninstallHandlers.SilentGaveUp);
        Assert.Empty(visibleRuns);
        var snapshot = JsonSerializer.Deserialize(first.Payload!, UninstallJson.Default.LeftoverSnapshot)!;
        Assert.True(snapshot.ProgramStillInstalled);

        var second = await h.HandleUninstall(new WorkerRequest
        {
            Op = Ops.Uninstall,
            Target = b.Program.Id,
            UserApproved = true,
            Items = [UninstallHandlers.SkipRestorePoint, UninstallHandlers.AutoClean, UninstallHandlers.VisibleOnly],
        }, progress, default);

        Assert.True(second.Ok, second.Message);
        Assert.DoesNotContain(second.Items, i => i.Path == UninstallHandlers.SilentGaveUp);
        Assert.Equal([$"\"{b.Unins}\""], visibleRuns);
    }
}
