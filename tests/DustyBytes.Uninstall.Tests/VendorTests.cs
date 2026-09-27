using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;

namespace DustyBytes.Uninstall.Tests;

[Collection("DryRunEnv")]
public class VendorTests : IDisposable
{
    readonly string? _saved = Environment.GetEnvironmentVariable(DryRun.Variable);

    public VendorTests() => Environment.SetEnvironmentVariable(DryRun.Variable, null);

    public void Dispose() => Environment.SetEnvironmentVariable(DryRun.Variable, _saved);

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
    [InlineData(InstallerType.Inno, @"""C:\X\unins000.exe""", @"""C:\X\unins000.exe"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART")]
    [InlineData(InstallerType.Nsis, @"""C:\X\uninstall.exe""", @"""C:\X\uninstall.exe"" /S")]
    [InlineData(InstallerType.Burn, @"""C:\ProgramData\Package Cache\{A}\setup.exe"" /uninstall", @"""C:\ProgramData\Package Cache\{A}\setup.exe"" /uninstall /quiet /norestart")]
    [InlineData(InstallerType.Squirrel, @"C:\Users\u\AppData\Local\X\Update.exe --uninstall", @"C:\Users\u\AppData\Local\X\Update.exe --uninstall -s")]
    public void SilentFlagsFollowInstallerType(InstallerType type, string uninstall, string expected)
    {
        var silent = Uninstaller.SilentCommand(P(type, uninstall))!;
        Assert.True(silent.Silent);
        Assert.Equal(expected, silent.CommandLine);
        Assert.Equal(uninstall, Uninstaller.VisibleCommand(P(type, uninstall))!.CommandLine);
    }

    [Fact]
    public void QuietStringWinsAndUnknownHasNoSilentForm()
    {
        const string code = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        Assert.Equal("q.exe /quiet", Uninstaller.SilentCommand(P(InstallerType.Inno, "u.exe", "q.exe /quiet"))!.CommandLine);
        Assert.Null(Uninstaller.SilentCommand(P(InstallerType.InstallShield, @"""C:\X\setup.exe"" -runfromtemp")));
        Assert.Null(Uninstaller.SilentCommand(P(InstallerType.Unknown, "remove.exe")));
        Assert.Equal("q.exe /quiet", Uninstaller.SilentCommand(P(InstallerType.Msi, "u.exe", "q.exe /quiet") with { WindowsInstaller = true, ProductCode = code })!.CommandLine);
        Assert.EndsWith(@"/l*v ""C:\log.txt""", Uninstaller.SilentCommand(P(InstallerType.Msi, "MsiExec.exe /X" + code) with { WindowsInstaller = true, ProductCode = code }, @"C:\log.txt")!.CommandLine);
    }

    [Fact]
    public void BurnAndSquirrelAreDetected()
    {
        var reg = new FakeRegistryView();
        var key = Fixture.UninstallKey(reg, "{B}");
        reg.Set(key, "BundleCachePath", @"C:\ProgramData\Package Cache\{B}\setup.exe");
        Assert.Equal(InstallerType.Burn, InstallerDetector.Detect("{B}", false, @"""C:\x\setup.exe"" /uninstall", null, reg, key, null));
        Assert.Equal(InstallerType.Burn, InstallerDetector.Detect("B", false, @"""C:\ProgramData\Package Cache\{B}\setup.exe"" /uninstall", null, null, null, null));
        Assert.Equal(InstallerType.Squirrel, InstallerDetector.Detect("S", false, @"""C:\Users\u\AppData\Local\S\Update.exe"" --uninstall", null, null, null, null));
    }

    sealed class Bench : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly FakeRegistryView Reg = new();
        public readonly RegKeyRef Key;
        public readonly string Unins;
        public readonly InstalledProgram Program;
        public readonly List<RunRequest> Runs = [];
        public readonly List<ScanProgress> Steps = [];

        public Bench()
        {
            var dir = Tree.Dir("Zqxv");
            Unins = Tree.File(@"Zqxv\unins000.exe");
            Key = Fixture.UninstallKey(Reg, "Zqxv_is1");
            Reg.SetAll(Key, ("DisplayName", "Zqxv"), ("UninstallString", $"\"{Unins}\""));
            Program = Fixture.Program("Zqxv", dir, null, Key, $"\"{Unins}\"") with { Installer = InstallerType.Inno };
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

        public IProgress<ScanProgress> Progress => new Sync(Steps.Add);

        sealed class Sync(Action<ScanProgress> a) : IProgress<ScanProgress>
        {
            public void Report(ScanProgress value) => a(value);
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public async Task SilentSuccessNeedsNoWindow()
    {
        using var b = new Bench();
        var u = b.Make((req, n) =>
        {
            b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program, b.Progress);

        Assert.True(r.Ok);
        var run = Assert.Single(b.Runs);
        Assert.Contains("/VERYSILENT", run.CommandLine);
        Assert.NotNull(run.Timeout);
        Assert.DoesNotContain(b.Steps, s => s.Step == Uninstaller.VisibleStep);
    }

    [Fact]
    public async Task SilentRunThatLeavesProgramFallsBackToVisibleWithoutTimeout()
    {
        using var b = new Bench();
        var u = b.Make((req, n) =>
        {
            if (n == 2)
                b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program, b.Progress);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(2, b.Runs.Count);
        Assert.Equal($"\"{b.Unins}\"", b.Runs[1].CommandLine);
        Assert.Null(b.Runs[1].Timeout);
        Assert.Contains(b.Steps, s => s.Step == Uninstaller.VisibleStep && s.Line == Uninstaller.VisibleCard);
    }

    [Fact]
    public async Task SilentTimeoutFallsBackToVisible()
    {
        using var b = new Bench();
        var u = b.Make((req, n) =>
        {
            if (n == 1)
                return new ProcessRunResult(true, false, -1, "süre doldu", TimedOut: true);
            b.Remove();
            return new ProcessRunResult(true, true, 0, "ok");
        });

        var r = await u.RunVendorUninstaller(b.Program, b.Progress);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(2, b.Runs.Count);
        Assert.Contains("Sessiz kaldırma olmadı", r.Message);
    }

    [Fact]
    public async Task ExitWithProgramGoneCountsAsDone()
    {
        using var b = new Bench();
        var u = b.Make((req, n) =>
        {
            b.Remove();
            return new ProcessRunResult(true, true, 5, "garip kod");
        });

        var r = await u.RunVendorUninstaller(b.Program, b.Progress);

        Assert.True(r.Ok);
        Assert.Single(b.Runs);
    }
}
