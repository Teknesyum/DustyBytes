using System.Diagnostics;
using DustyBytes.Clean.Uninstall;
using Xunit.Abstractions;

namespace DustyBytes.Uninstall.Tests;

public class MiscTests
{
    static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void ShortcutTargetIsParsed()
    {
        var target = ShellLink.ReadTarget(FixturePath("acme.lnk"));
        Assert.NotNull(target);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", target, ignoreCase: true);
    }

    [Fact]
    public void ShortcutWithEnvironmentTargetIsExpanded()
    {
        var target = ShellLink.ReadTarget(FixturePath("acme-env.lnk"));
        Assert.NotNull(target);
        Assert.EndsWith(@"\System32\calc.exe", target, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", target);
    }

    [Fact]
    public void GarbageIsNotAShortcut()
    {
        Assert.Null(ShellLink.ParseTarget([1, 2, 3]));
        Assert.Null(ShellLink.ParseTarget(new byte[200]));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Acme\unins000.exe"" /SILENT", @"C:\Program Files\Acme\unins000.exe")]
    [InlineData(@"C:\Acme\uninstall.exe /S", @"C:\Acme\uninstall.exe")]
    [InlineData(@"MsiExec.exe /X{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", "MsiExec.exe")]
    public void ExecutableFromCommand(string command, string expected) =>
        Assert.Equal(expected, CommandLine.Executable(command), ignoreCase: true);

    [Fact]
    public void PathsInsideCommand()
    {
        var paths = CommandLine.Paths(@"rundll32.exe ""C:\Program Files\Acme\a.dll"",Entry C:\Acme\b.exe");
        Assert.Contains(paths, p => p.Equals(@"C:\Program Files\Acme\a.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.StartsWith(@"C:\Acme\b.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DevicePrefixesAreCleaned()
    {
        Assert.Equal(@"C:\Drivers\x.sys", CommandLine.Clean(@"\??\C:\Drivers\x.sys"), ignoreCase: true);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), CommandLine.Clean(@"\SystemRoot\System32\drivers\y.sys"), StringComparison.OrdinalIgnoreCase);
    }

    static InstalledProgram P(bool msi = false, string? quiet = null, string? uninstall = null, string? code = null) => new()
    {
        Id = "reg:HKLM64:x",
        DisplayName = "X",
        Source = ProgramSource.Registry,
        WindowsInstaller = msi,
        ProductCode = code,
        QuietUninstallString = quiet,
        UninstallString = uninstall,
        Installer = msi ? InstallerType.Msi : InstallerType.Unknown,
    };

    [Fact]
    public void VendorCommandPreference()
    {
        Assert.Equal("q.exe /S", Uninstaller.BuildCommand(P(quiet: "q.exe /S", uninstall: "u.exe"))!.CommandLine);
        Assert.True(Uninstaller.BuildCommand(P(quiet: "q.exe /S"))!.Silent);
        var visible = Uninstaller.BuildCommand(P(uninstall: "u.exe"))!;
        Assert.False(visible.Silent);
        Assert.Equal("msiexec.exe /x {AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE} /qb /norestart",
            Uninstaller.BuildCommand(P(msi: true, uninstall: "MsiExec.exe /I{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", code: "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"))!.CommandLine);
        Assert.Null(Uninstaller.BuildCommand(P()));
    }

    [Fact]
    public void MsixRemovalRefusesWildcards()
    {
        var r = MsixPackages.RemoveAsync("Microsoft.*", null, default).GetAwaiter().GetResult();
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData(true, 0, true, false)]
    [InlineData(true, 3010, true, true)]
    [InlineData(true, 1641, true, true)]
    [InlineData(true, 1605, true, false)]
    [InlineData(true, 1602, false, false)]
    [InlineData(true, 1618, false, false)]
    [InlineData(false, 3010, false, false)]
    [InlineData(false, 1, false, false)]
    public void ExitCodes(bool msi, int code, bool ok, bool reboot)
    {
        var r = Uninstaller.Interpret(P(msi: msi), code);
        Assert.Equal(ok, r.Ok);
        Assert.Equal(reboot, r.Reboot);
    }

    [Fact]
    public async Task NoRemoveProgramIsNotRun()
    {
        var ran = false;
        var u = new Uninstaller(new LeftoverScanner(Fixture.Context(new FakeRegistryView(), new FakeProbe(), [], [])))
        {
            Runner = (_, _, _, _) =>
            {
                ran = true;
                return Task.FromResult(new ProcessRunResult(true, true, 0, ""));
            },
        };
        var r = await u.RunVendorUninstaller(P(uninstall: "u.exe") with { NoRemove = true });
        Assert.False(r.Ok);
        Assert.False(ran);
    }

    [Fact]
    public void BroadPathsRejectRootsAndAncestors()
    {
        var roots = new[] { @"C:\Users\u\AppData\Roaming", @"C:\Program Files" };
        Assert.True(BroadPaths.IsTooBroad(@"C:\", roots));
        Assert.True(BroadPaths.IsTooBroad(@"C:\Users\u", roots));
        Assert.True(BroadPaths.IsTooBroad(@"C:\Program Files", roots));
        Assert.False(BroadPaths.IsTooBroad(@"C:\Program Files\Acme", roots));
    }
}

public class LiveTests(ITestOutputHelper output)
{
    [Fact]
    public void EnumerateRealMachineOnce()
    {
        if (Environment.GetEnvironmentVariable("DUSTYBYTES_LIVE") != "1")
            return;
        var sw = Stopwatch.StartNew();
        var list = InstalledPrograms.Enumerate();
        sw.Stop();
        var msi = list.Count(p => p.Installer == InstallerType.Msi);
        var msix = list.Count(p => p.Source == ProgramSource.Msix);
        var byType = string.Join(", ", list.GroupBy(p => p.Installer).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"));
        var measured = list.Count(p => p.SizeMeasured);
        var line = $"LIVE toplam={list.Count} msi={msi} msix={msix} olcum={measured} sure={sw.ElapsedMilliseconds}ms tur=[{byType}]";
        output.WriteLine(line);
        var file = Environment.GetEnvironmentVariable("DUSTYBYTES_LIVE_OUT");
        if (!string.IsNullOrEmpty(file))
            File.WriteAllText(file, line);
        Assert.NotEmpty(list);
    }
}
