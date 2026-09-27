using System.Text.Json;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Uninstall.Tests;

[CollectionDefinition("DryRunEnv", DisableParallelization = true)]
public class DryRunEnvCollection;

[Collection("DryRunEnv")]
public class RemovalTests : IDisposable
{
    readonly string? _saved = Environment.GetEnvironmentVariable(DryRun.Variable);

    public RemovalTests() => Environment.SetEnvironmentVariable(DryRun.Variable, null);

    public void Dispose() => Environment.SetEnvironmentVariable(DryRun.Variable, _saved);

    sealed class Setup : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly FakeRegistryView Reg = new();
        public readonly FakeSystemActions Actions = new();
        public readonly List<string> Quarantined = [];
        public readonly RegKeyRef Key;
        public readonly RegKeyRef Rules;
        public readonly string Dir;
        public readonly string Unins;
        public readonly InstalledProgram Program;
        public readonly LeftoverScanner Scanner;

        public Setup()
        {
            var bases = Tree.Dir("Programs");
            Dir = Tree.Dir(@"Programs\ZqxvWidget");
            Unins = Tree.File(@"Programs\ZqxvWidget\unins000.exe");
            var exe = Tree.File(@"Programs\ZqxvWidget\widget.exe", "x");
            Key = Fixture.UninstallKey(Reg, "ZqxvWidget_is1");
            Reg.SetAll(Key, ("DisplayName", "Zqxv Widget"), ("UninstallString", $"\"{Unins}\""), ("InstallLocation", Dir), ("Publisher", "Zqxv Ltd"));
            Rules = Reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.FirewallRulesPath);
            Reg.Set(Rules, "{R1}", $"v2.30|Action=Allow|App={exe}|Name=Zqxv Widget|");
            Program = Fixture.Program("Zqxv Widget", Dir, "Zqxv Ltd", Key, $"\"{Unins}\"") with { KeyName = "ZqxvWidget_is1", Installer = InstallerType.Inno };
            Scanner = new LeftoverScanner(Fixture.Context(Reg, new FakeProbe(), [Program], [bases]));
        }

        public string BackupDir => Path.Combine(Tree.Root, "backup");

        public LeftoverRemover Remover() => new(Reg, Scanner, p =>
        {
            Quarantined.Add(p);
            return Task.FromResult(true);
        }, Actions, BackupDir);

        public LeftoverSnapshot Uninstalled()
        {
            var before = Scanner.Snapshot(Program);
            Reg.DeleteKeyTree(Key);
            Reg.Deleted.Clear();
            File.Delete(Unins);
            return Scanner.Diff(before);
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public async Task OnlyApprovedItemsAreTouched()
    {
        using var s = new Setup();
        var snap = s.Uninstalled();
        var folder = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        var rule = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.FirewallRule);

        var report = await s.Remover().Remove(snap, [folder.Id, "folder:c:\\windows"]);

        Assert.Equal([Paths.Normalize(s.Dir)], s.Quarantined.Select(Paths.Normalize));
        Assert.Empty(s.Actions.Calls);
        Assert.True(s.Reg.ValueExists(s.Rules, "{R1}"));
        Assert.Equal(1, report.Removed);
        Assert.Single(report.Items, i => !i.Ok && i.Target == "folder:c:\\windows");
        Assert.Null(report.RegBackupFile);
        Assert.DoesNotContain(report.Items, i => i.Id == rule.Id);
    }

    [Fact]
    public async Task RegistryItemsAreExportedBeforeRemoval()
    {
        using var s = new Setup();
        var snap = s.Uninstalled();
        var rule = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.FirewallRule);

        var report = await s.Remover().Remove(snap, [rule.Id]);

        Assert.NotNull(report.RegBackupFile);
        var bytes = File.ReadAllBytes(report.RegBackupFile!);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        var text = System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        Assert.Contains("\"{R1}\"=", text);
        Assert.Equal(["firewall:{R1}"], s.Actions.Calls);
        Assert.Empty(s.Quarantined);
    }

    [Fact]
    public async Task DryRunOnlyLogs()
    {
        using var s = new Setup();
        var snap = s.Uninstalled();
        Environment.SetEnvironmentVariable(DryRun.Variable, "1");

        var remover = s.Remover();
        var report = await remover.Remove(snap, snap.Candidates.Select(c => c.Id).ToList());

        Assert.True(report.DryRun);
        Assert.Empty(s.Quarantined);
        Assert.Empty(s.Actions.Calls);
        Assert.Empty(s.Reg.Deleted);
        Assert.Null(report.RegBackupFile);
        Assert.False(Directory.Exists(s.BackupDir));
        Assert.NotEmpty(remover.Log);
        Assert.All(report.Items, i => Assert.True(i.Ok));
    }

    [Fact]
    public async Task FolderThatBecameUserDataIsRefusedAtRemovalTime()
    {
        using var s = new Setup();
        var snap = s.Uninstalled();
        var folder = Assert.Single(snap.Candidates, c => c.Kind == LeftoverKind.Folder);
        var ctx = Fixture.Context(s.Reg, new FakeProbe(), [], [], userData: [Path.GetDirectoryName(s.Dir)!]);
        var remover = new LeftoverRemover(s.Reg, new LeftoverScanner(ctx), p =>
        {
            s.Quarantined.Add(p);
            return Task.FromResult(true);
        }, s.Actions, s.BackupDir);

        var report = await remover.Remove(snap, [folder.Id]);

        Assert.Empty(s.Quarantined);
        Assert.False(Assert.Single(report.Items).Ok);
    }

    static UninstallHandlers Handlers(Setup s, SnapshotStore store, Func<string, Task<bool>>? quarantine = null, Action? onEnumerate = null, bool removes = true) =>
        new(Fixture.EmptyProtection(), quarantine, s.Reg, new FakeProbe(), store, s.Actions)
        {
            ProgramProvider = () =>
            {
                onEnumerate?.Invoke();
                return [s.Program];
            },
            UninstallerFactory = scanner => new Uninstaller(scanner)
            {
                Runner = (req, ct) =>
                {
                    if (!removes)
                        return Task.FromResult(new ProcessRunResult(true, true, 0, "sahte"));
                    s.Reg.DeleteKeyTree(s.Key);
                    File.Delete(s.Unins);
                    return Task.FromResult(new ProcessRunResult(true, true, 0, "sahte"));
                },
            },
        };

    [Fact]
    public async Task HandlersRejectWithoutUserApproval()
    {
        using var s = new Setup();
        var store = new SnapshotStore(Path.Combine(s.Tree.Root, "store"));
        var enumerated = false;
        var h = Handlers(s, store, onEnumerate: () => enumerated = true);
        var progress = new Progress<WorkerProgress>();

        var u = await h.HandleUninstall(new WorkerRequest { Op = Ops.Uninstall, Target = s.Program.Id, UserApproved = false }, progress, default);
        var r = await h.HandleRemoveLeftovers(new WorkerRequest { Op = Ops.RemoveLeftovers, UnitId = Guid.NewGuid().ToString("N"), Items = ["x"], UserApproved = false }, progress, default);

        Assert.False(u.Ok);
        Assert.False(r.Ok);
        Assert.False(enumerated);
        Assert.True(s.Reg.KeyExists(s.Key));
        Assert.Empty(s.Actions.Calls);
    }

    [Fact]
    public async Task RemoveLeftoversRefusesUnknownOrNonDiffSnapshots()
    {
        using var s = new Setup();
        var store = new SnapshotStore(Path.Combine(s.Tree.Root, "store"));
        var h = Handlers(s, store);
        var progress = new Progress<WorkerProgress>();
        var before = s.Scanner.Snapshot(s.Program);
        store.Save(before);

        var unknown = await h.HandleRemoveLeftovers(new WorkerRequest { Op = Ops.RemoveLeftovers, UnitId = Guid.NewGuid().ToString("N"), Items = ["x"], UserApproved = true }, progress, default);
        var traversal = await h.HandleRemoveLeftovers(new WorkerRequest { Op = Ops.RemoveLeftovers, UnitId = @"..\..\x", Items = ["x"], UserApproved = true }, progress, default);
        var notDiff = await h.HandleRemoveLeftovers(new WorkerRequest { Op = Ops.RemoveLeftovers, UnitId = before.Id, Items = [before.Candidates[0].Id], UserApproved = true }, progress, default);

        Assert.False(unknown.Ok);
        Assert.False(traversal.Ok);
        Assert.False(notDiff.Ok);
        Assert.Empty(s.Actions.Calls);
    }

    [Fact]
    public async Task UninstallThenRemoveApprovedLeftoversEndToEnd()
    {
        using var s = new Setup();
        var store = new SnapshotStore(Path.Combine(s.Tree.Root, "store"));
        var quarantined = new List<string>();
        var h = Handlers(s, store, p =>
        {
            quarantined.Add(p);
            return Task.FromResult(true);
        });
        var progress = new Progress<WorkerProgress>();

        var u = await h.HandleUninstall(new WorkerRequest { Op = Ops.Uninstall, Target = s.Program.Id, UserApproved = true, Items = [UninstallHandlers.SkipRestorePoint] }, progress, default);

        Assert.True(u.Ok, u.Message);
        var diff = JsonSerializer.Deserialize(u.Payload!, UninstallJson.Default.LeftoverSnapshot)!;
        Assert.True(diff.IsDiff);
        Assert.False(diff.ProgramStillInstalled);
        var folder = Assert.Single(diff.Candidates, c => c.Kind == LeftoverKind.Folder);

        var r = await h.HandleRemoveLeftovers(new WorkerRequest { Op = Ops.RemoveLeftovers, UnitId = diff.Id, Items = [folder.Id], UserApproved = true }, progress, default);

        Assert.True(r.Ok, r.Message);
        Assert.Equal([Paths.Normalize(s.Dir)], quarantined.Select(Paths.Normalize));
        Assert.True(s.Reg.ValueExists(s.Rules, "{R1}"));
    }

    [Fact]
    public async Task AutoCleanQuarantinesOnlyHighPathEvidenceAndKeepsTheRest()
    {
        using var s = new Setup();
        var store = new SnapshotStore(Path.Combine(s.Tree.Root, "store"));
        var quarantined = new List<string>();
        var h = Handlers(s, store, p =>
        {
            quarantined.Add(p);
            return Task.FromResult(true);
        });

        var u = await h.HandleUninstall(new WorkerRequest { Op = Ops.Uninstall, Target = s.Program.Id, UserApproved = true, Items = [UninstallHandlers.SkipRestorePoint, UninstallHandlers.AutoClean] }, new Progress<WorkerProgress>(), default);

        Assert.True(u.Ok, u.Message);
        var item = Assert.Single(u.Items, i => i.Path == UninstallHandlers.AutoClean);
        Assert.True(item.Ok, item.Message);
        var diff = JsonSerializer.Deserialize(u.Payload!, UninstallJson.Default.LeftoverSnapshot)!;
        Assert.Contains(Paths.Normalize(s.Dir), quarantined.Select(Paths.Normalize));
        Assert.Contains(diff.AutoRemoved, r => r.Ok && r.Kind == LeftoverKind.Folder);
        Assert.DoesNotContain(diff.Candidates, c => c.Kind == LeftoverKind.Folder);
        Assert.All(diff.Candidates, c => Assert.False(c.AutoRemovable));
        Assert.Equal(diff.Id, store.Load(diff.Id)!.Id);
    }

    [Fact]
    public async Task AutoCleanDoesNothingWhileProgramIsStillInstalled()
    {
        using var s = new Setup();
        var store = new SnapshotStore(Path.Combine(s.Tree.Root, "store"));
        var quarantined = new List<string>();
        var h = Handlers(s, store, p =>
        {
            quarantined.Add(p);
            return Task.FromResult(true);
        }, removes: false);

        var u = await h.HandleUninstall(new WorkerRequest { Op = Ops.Uninstall, Target = s.Program.Id, UserApproved = true, Items = [UninstallHandlers.SkipRestorePoint, UninstallHandlers.AutoClean] }, new Progress<WorkerProgress>(), default);

        Assert.False(Assert.Single(u.Items, i => i.Path == UninstallHandlers.AutoClean).Ok);
        Assert.Empty(quarantined);
        Assert.Empty(s.Actions.Calls);
        Assert.DoesNotContain(s.Reg.Deleted, d => !d.Contains("ZqxvWidget_is1"));
    }

    [Fact]
    public void FoldersUnderSettingsBasesAreMarkedAsSettings()
    {
        using var s = new Setup();
        var roaming = s.Tree.Dir("Roaming");
        var folder = s.Tree.Dir(@"Roaming\Zqxv Widget");
        s.Tree.File(@"Roaming\Zqxv Widget\settings.json", "{}");
        var ctx = Fixture.Context(s.Reg, new FakeProbe(), [s.Program], [roaming], settings: [roaming]);

        var snap = new LeftoverScanner(ctx).Snapshot(s.Program);

        var c = Assert.Single(snap.Candidates, c => Paths.Normalize(c.Target) == Paths.Normalize(folder));
        Assert.True(c.IsSettings);
        Assert.DoesNotContain(snap.Candidates, c => c.IsSettings && Paths.Normalize(c.Target) == Paths.Normalize(s.Dir));
    }

    [Fact]
    public async Task RemoverRefusesBlockedRegistryAndStaleValues()
    {
        using var s = new Setup();
        var ext = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes\.zqxv");
        s.Reg.Set(ext, "", "Zqxv.Doc");
        var office = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Microsoft\Office");
        var assoc = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes\.zqxw");
        s.Reg.Set(assoc, "", "Other.Doc");
        var snap = s.Uninstalled() with
        {
            Candidates =
            [
                new LeftoverCandidate { Id = "ext", Kind = LeftoverKind.RegistryKey, Target = ext.Display, Key = ext, Tier = ConfidenceTier.High },
                new LeftoverCandidate { Id = "office", Kind = LeftoverKind.RegistryKey, Target = office.Display, Key = office, Tier = ConfidenceTier.High },
                new LeftoverCandidate { Id = "stale", Kind = LeftoverKind.RegistryValue, Target = assoc.Display, Key = assoc, ValueName = "", ExpectValue = "Zqxv.Doc", Tier = ConfidenceTier.Medium },
            ],
        };

        var report = await s.Remover().Remove(snap, ["ext", "office", "stale"]);

        Assert.All(report.Items, i => Assert.False(i.Ok));
        Assert.True(s.Reg.KeyExists(ext));
        Assert.True(s.Reg.KeyExists(office));
        Assert.Equal("Other.Doc", s.Reg.GetString(assoc, ""));
        Assert.Empty(s.Reg.Deleted);
    }

    [Fact]
    public async Task EmptyPublisherKeyIsBackedUpAndRemovedAfterItsLastProduct()
    {
        using var s = new Setup();
        var product = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Zqxv Ltd\Widget");
        s.Reg.Set(product, "Path", s.Dir);
        var other = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Qwv Corp\Widget");
        s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Qwv Corp\Other");
        var snap = s.Uninstalled() with
        {
            Candidates =
            [
                new LeftoverCandidate { Id = "p1", Kind = LeftoverKind.RegistryKey, Target = product.Display, Key = product, Tier = ConfidenceTier.High },
                new LeftoverCandidate { Id = "p2", Kind = LeftoverKind.RegistryKey, Target = other.Display, Key = other, Tier = ConfidenceTier.High },
            ],
        };

        var report = await s.Remover().Remove(snap, ["p1", "p2"]);

        Assert.False(s.Reg.KeyExists(product.Parent()!));
        Assert.True(s.Reg.KeyExists(other.Parent()!));
        Assert.Contains(report.Items, i => i.Id.StartsWith("parent:") && i.Ok);
        Assert.True(File.Exists(Path.ChangeExtension(report.RegBackupFile!, null) + "-parents.reg"));
    }

    [Fact]
    public void FolderHoldingSharedDllWithCountAboveOneIsNeverACandidate()
    {
        using var s = new Setup();
        var dll = s.Tree.File(@"Programs\ZqxvWidget\common.dll", "x");
        var shared = s.Reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.SharedDllsPath);
        s.Reg.Set(shared, dll, 2);

        var snap = s.Uninstalled();

        Assert.DoesNotContain(snap.Candidates, c => c.Kind == LeftoverKind.Folder && Paths.Normalize(c.Target) == Paths.Normalize(s.Dir));
        Assert.Contains(snap.Blocked, b => b.Reason.Contains("SharedDLLs"));
    }
}
