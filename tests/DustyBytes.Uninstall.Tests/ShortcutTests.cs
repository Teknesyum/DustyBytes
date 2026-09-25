using System.Buffers.Binary;
using System.Text;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Protection;

namespace DustyBytes.Uninstall.Tests;

public class ShortcutTests
{
    static byte[] Link(string target)
    {
        var path = Encoding.Latin1.GetBytes(target + "\0");
        var info = new byte[0x1C + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(info, (uint)info.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), 0x1C);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(16), 0x1C);
        path.CopyTo(info, 0x1C);
        var header = new byte[76];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4C);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 0x2);
        return [.. header, .. info];
    }

    sealed class Setup : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly string Bases;
        public readonly string Install;
        public readonly string Exe;
        public readonly string StartMenu;
        public readonly string Desktop;
        public readonly InstalledProgram Program;
        public readonly FakeRegistryView Reg = new();
        public readonly ScanContext Context;

        public Setup(string? location = null)
        {
            Bases = Tree.Dir("Programs");
            Install = Tree.Dir(@"Programs\AcmeWidget");
            Exe = Tree.File(@"Programs\AcmeWidget\widget.exe");
            StartMenu = Tree.Dir(@"StartMenu\Programs");
            Desktop = Tree.Dir("Desktop");
            Program = Fixture.Program("Acme Widget", location ?? Install, "Acme Inc.");
            var protection = new ProtectedList(new ProtectedRules
            {
                Roots = [new PathRule { Path = Path.GetDirectoryName(StartMenu)!, Reason = "Başlat menüsü" }],
                NeverLeftover = [Desktop],
            });
            Context = new ScanContext
            {
                Registry = Reg,
                Probe = new FakeProbe(),
                Protection = protection,
                Programs = [Program],
                UserDataRoots = [Paths.Normalize(Desktop)],
                DataBases = [Paths.Normalize(Bases)],
                ShortcutDirs = [Paths.Normalize(StartMenu)],
                DesktopDirs = [Paths.Normalize(Desktop)],
                BroadRoots = [Paths.Normalize(Bases), Paths.Normalize(StartMenu), Paths.Normalize(Desktop)],
            };
        }

        public string Lnk(string folder, string name, string target)
        {
            var p = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, Link(target));
            return Paths.Normalize(p);
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void StartMenuAndDesktopShortcutsOfTheProgramAreHighCandidates()
    {
        using var s = new Setup();
        var menu = s.Lnk(s.StartMenu, @"Acme\Acme Widget.lnk", s.Exe);
        var desk = s.Lnk(s.Desktop, "Acme Widget.lnk", s.Exe);

        var snap = new LeftoverScanner(s.Context).Snapshot(s.Program);

        foreach (var lnk in new[] { menu, desk })
        {
            var c = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Shortcut && c.Target.Equals(lnk, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ConfidenceTier.High, c.Tier);
            Assert.True(c.Checked);
            Assert.False(string.IsNullOrWhiteSpace(c.Reason));
        }
        Assert.DoesNotContain(snap.Blocked, b => b.Kind == LeftoverKind.Shortcut);
    }

    [Fact]
    public void ShortcutsToOtherTargetsStayOut()
    {
        using var s = new Setup();
        var other = s.Tree.File(@"Programs\Other\other.exe");
        s.Lnk(s.StartMenu, "Other.lnk", other);
        s.Lnk(s.Desktop, "Other.lnk", other);

        var snap = new LeftoverScanner(s.Context).Snapshot(s.Program);

        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Shortcut);
    }

    [Fact]
    public void StartMenuFolderItselfStaysProtected()
    {
        using var s = new Setup();
        var scanner = new LeftoverScanner(s.Context);

        Assert.NotNull(scanner.GateFolder(s.StartMenu));
        Assert.NotNull(scanner.GateFolder(Path.Combine(s.StartMenu, "Acme")));
        Assert.False(scanner.IsProgramShortcut(Path.Combine(s.StartMenu, "Acme"), s.Exe, s.Program));
        Assert.False(scanner.IsProgramShortcut(Path.Combine(s.StartMenu, "readme.txt"), s.Exe, s.Program));
    }

    [Fact]
    public void DesktopSubfolderShortcutIsNotExempt()
    {
        using var s = new Setup();
        var nested = s.Lnk(s.Desktop, @"Projects\Acme Widget.lnk", s.Exe);

        Assert.False(new LeftoverScanner(s.Context).IsProgramShortcut(nested, s.Exe, s.Program));
    }

    [Fact]
    public void WithoutInstallLocationShortcutsStayBlocked()
    {
        using var s = new Setup();
        var program = s.Program with { InstallLocation = null, UninstallString = Path.Combine(s.Install, "unins000.exe") };
        var menu = s.Lnk(s.StartMenu, "Acme Widget.lnk", s.Exe);
        var scanner = new LeftoverScanner(s.Context);

        Assert.False(scanner.IsProgramShortcut(menu, s.Exe, program));
        var snap = scanner.Snapshot(program);
        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Shortcut);
    }

    [Fact]
    public async Task ShortcutGoesToQuarantineOnRemoval()
    {
        using var s = new Setup();
        var menu = s.Lnk(s.StartMenu, "Acme Widget.lnk", s.Exe);
        var scanner = new LeftoverScanner(s.Context);
        var snap = scanner.Snapshot(s.Program);
        var c = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Shortcut);
        var sent = new List<string>();
        var remover = new LeftoverRemover(s.Reg, scanner, p =>
        {
            sent.Add(p);
            return Task.FromResult(true);
        }, backupDir: s.Tree.Dir("backup"));

        var report = await remover.Remove(snap, [c.Id]);

        var item = Assert.Single(report.Items);
        Assert.True(item.Ok);
        Assert.Equal(menu, Assert.Single(sent), ignoreCase: true);
    }

    [Fact]
    public void DiffKeepsProgramShortcut()
    {
        using var s = new Setup();
        s.Lnk(s.StartMenu, "Acme Widget.lnk", s.Exe);
        var scanner = new LeftoverScanner(s.Context);
        var before = scanner.Snapshot(s.Program);

        var after = scanner.Diff(before);

        Assert.Contains(after.Candidates, c => c.Kind == LeftoverKind.Shortcut && c.Tier == ConfidenceTier.High);
    }
}
