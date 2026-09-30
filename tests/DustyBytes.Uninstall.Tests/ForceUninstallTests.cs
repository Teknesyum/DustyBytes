using System.Text.Json;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Uninstall.Tests;

[Collection("DryRunEnv")]
public class ForceUninstallTests : IDisposable
{
    readonly string? _saved = Environment.GetEnvironmentVariable(DryRun.Variable);

    public ForceUninstallTests() => Environment.SetEnvironmentVariable(DryRun.Variable, null);

    public void Dispose() => Environment.SetEnvironmentVariable(DryRun.Variable, _saved);

    sealed class Setup : IDisposable
    {
        public readonly TempTree Tree = new();
        public readonly FakeRegistryView Reg = new();
        public readonly FakeSystemActions Actions = new();
        public readonly List<string> Quarantined = [];
        public readonly RegKeyRef Key;
        public readonly string Dir;
        public readonly string Unins;
        public InstalledProgram Program;
        public bool QuarantineWorks = true;
        public int VendorRuns;

        public Setup(string name = "Zqxv Widget", bool brokenUninstaller = true)
        {
            Tree.Dir("Programs");
            Dir = Tree.Dir(@"Programs\ZqxvWidget");
            Unins = Tree.File(@"Programs\ZqxvWidget\unins000.exe");
            var exe = Tree.File(@"Programs\ZqxvWidget\widget.exe", "x");
            Key = Fixture.UninstallKey(Reg, "ZqxvWidget_is1");
            Reg.SetAll(Key, ("DisplayName", name), ("UninstallString", $"\"{Unins}\""), ("InstallLocation", Dir), ("Publisher", "Zqxv Ltd"));
            var rules = Reg.Key(RegHive.LocalMachine, RegView.Registry64, LeftoverScanner.FirewallRulesPath);
            Reg.Set(rules, "{R1}", $"v2.30|Action=Allow|App={exe}|Name={name}|");
            if (brokenUninstaller)
                File.Delete(Unins);
            Program = Fixture.Program(name, Dir, "Zqxv Ltd", Key, $"\"{Unins}\"") with { KeyName = "ZqxvWidget_is1", Installer = InstallerType.Inno };
        }

        public string BackupDir => Path.Combine(Tree.Root, "backup");

        public UninstallHandlers Handlers() =>
            new(Fixture.EmptyProtection(), p =>
            {
                if (!QuarantineWorks)
                    return Task.FromResult(false);
                Quarantined.Add(p);
                if (Directory.Exists(p))
                    Directory.Delete(p, true);
                else if (File.Exists(p))
                    File.Delete(p);
                return Task.FromResult(true);
            }, Reg, new FakeProbe(), new SnapshotStore(Path.Combine(Tree.Root, "store")), Actions)
            {
                ProgramProvider = () => [Program],
                BackupDir = BackupDir,
                UninstallerFactory = scanner => new Uninstaller(scanner)
                {
                    Runner = (req, ct) =>
                    {
                        VendorRuns++;
                        return Task.FromResult(new ProcessRunResult(true, true, 1603, "sahte hata"));
                    },
                },
            };

        public static WorkerRequest Force(InstalledProgram p, bool approved = true, params string[] flags) =>
            new() { Op = Ops.ForceUninstall, Target = p.Id, UserApproved = approved, Items = [UninstallHandlers.SkipRestorePoint, .. flags] };

        public void Dispose() => Tree.Dispose();
    }

    static LeftoverSnapshot Payload(WorkerResponse r) => JsonSerializer.Deserialize(r.Payload!, UninstallJson.Default.LeftoverSnapshot)!;

    [Fact]
    public async Task MissingUninstallerQuarantinesInstallFolderThenRemovesEntryWithBackup()
    {
        using var s = new Setup();
        var h = s.Handlers();

        var r = await h.HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(0, s.VendorRuns);
        Assert.Contains(Paths.Normalize(s.Dir), s.Quarantined.Select(Paths.Normalize));
        Assert.False(Directory.Exists(s.Dir));
        Assert.False(s.Reg.KeyExists(s.Key));
        Assert.True(Assert.Single(r.Items, i => i.Path == ForceUninstall.EntryStep).Ok);
        Assert.True(Assert.Single(r.Items, i => i.Path == ForceUninstall.FilesStep).Ok);
        var after = Payload(r);
        Assert.True(after.IsDiff);
        Assert.False(after.ProgramStillInstalled);
        Assert.All(after.Candidates, c => Assert.False(c.AutoRemovable));
        Assert.NotNull(after.RegBackupFile);
        var backup = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(after.RegBackupFile!));
        Assert.Contains("ZqxvWidget_is1", backup);
        Assert.StartsWith(s.BackupDir, after.RegBackupFile);
    }

    [Fact]
    public async Task EntryIsKeptWhenInstallFolderCannotBeMoved()
    {
        using var s = new Setup { QuarantineWorks = false };
        var h = s.Handlers();

        var r = await h.HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);

        Assert.False(r.Ok);
        Assert.True(s.Reg.KeyExists(s.Key));
        Assert.True(Directory.Exists(s.Dir));
        var entry = Assert.Single(r.Items, i => i.Path == ForceUninstall.EntryStep);
        Assert.False(entry.Ok);
        Assert.Contains("korundu", entry.Message);
        Assert.True(Payload(r).ProgramStillInstalled);
    }

    [Fact]
    public async Task WorkingUninstallerIsRefusedUntilItFails()
    {
        using var s = new Setup(brokenUninstaller: false);
        var h = s.Handlers();

        var early = await h.HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.False(early.Ok);
        Assert.Contains("normal kaldırmayı", early.Message);
        Assert.Empty(s.Quarantined);
        Assert.True(s.Reg.KeyExists(s.Key));

        var vendor = await h.HandleUninstall(new WorkerRequest { Op = Ops.Uninstall, Target = s.Program.Id, UserApproved = true, Items = [UninstallHandlers.SkipRestorePoint, UninstallHandlers.AutoClean] }, new Progress<WorkerProgress>(), default);
        Assert.False(vendor.Ok);
        Assert.True(s.VendorRuns > 0);
        Assert.True(h.VendorFailed(s.Program.Id));
        Assert.Empty(s.Quarantined);

        var forced = await h.HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.True(forced.Ok, forced.Message);
        Assert.False(s.Reg.KeyExists(s.Key));
        Assert.False(h.VendorFailed(s.Program.Id));
    }

    [Fact]
    public async Task RefusedWithoutApproval()
    {
        using var s = new Setup();
        var r = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program, approved: false), new Progress<WorkerProgress>(), default);
        Assert.False(r.Ok);
        Assert.Contains("onay", r.Message);
        Assert.Empty(s.Quarantined);
        Assert.True(s.Reg.KeyExists(s.Key));
    }

    [Fact]
    public async Task SystemComponentEntriesAreRefused()
    {
        using var s = new Setup();
        s.Reg.Set(s.Key, "SystemComponent", 1);
        var r = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.False(r.Ok);
        Assert.Contains("Sistem bileşeni", r.Message);
        Assert.Empty(s.Quarantined);
        Assert.True(s.Reg.KeyExists(s.Key));
    }

    [Theory]
    [InlineData("Zqxv Graphics Driver")]
    [InlineData("Security Update for Windows (KB5034441)")]
    [InlineData("Windows Media Player")]
    public async Task DriversUpdatesAndWindowsComponentsAreRefused(string name)
    {
        using var s = new Setup(name);
        if (name.StartsWith("Windows", StringComparison.Ordinal))
            s.Program = s.Program with { Publisher = "Microsoft Corporation" };
        var r = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.False(r.Ok);
        Assert.Contains("zorla kaldırılmaz", r.Message);
        Assert.Empty(s.Quarantined);
        Assert.True(s.Reg.KeyExists(s.Key));
    }

    [Fact]
    public async Task UpdateReleaseTypeAndParentKeyAreRefused()
    {
        using var s = new Setup();
        s.Reg.Set(s.Key, "ParentKeyName", "Zqxv Suite");
        var parent = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.False(parent.Ok);

        s.Reg.DeleteValue(s.Key, "ParentKeyName");
        s.Reg.Set(s.Key, "ReleaseType", "Security Update");
        var update = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.False(update.Ok);
        Assert.Empty(s.Quarantined);
    }

    [Fact]
    public async Task KeepSettingsLeavesSettingsFolders()
    {
        using var s = new Setup();
        var h = s.Handlers();
        var r = await h.HandleForceUninstall(Setup.Force(s.Program, true, UninstallHandlers.KeepSettings), new Progress<WorkerProgress>(), default);
        Assert.True(r.Ok, r.Message);
        Assert.All(Payload(r).AutoRemoved, i => Assert.True(i.Ok));
    }

    [Fact]
    public async Task DryRunTouchesNothing()
    {
        using var s = new Setup();
        Environment.SetEnvironmentVariable(DryRun.Variable, "1");
        var r = await s.Handlers().HandleForceUninstall(Setup.Force(s.Program), new Progress<WorkerProgress>(), default);
        Assert.True(r.DryRun);
        Assert.Empty(s.Quarantined);
        Assert.True(s.Reg.KeyExists(s.Key));
        Assert.True(Directory.Exists(s.Dir));
        Assert.False(Directory.Exists(s.BackupDir));
    }

    [Fact]
    public void BrokenReasonDetectsMissingOrAbsentUninstaller()
    {
        using var s = new Setup(brokenUninstaller: false);
        var probe = new FakeProbe();
        Assert.Null(ForceUninstall.BrokenReason(s.Program, s.Reg, probe));

        File.Delete(s.Unins);
        Assert.Contains("Kaldırıcı dosyası yok", ForceUninstall.BrokenReason(s.Program, s.Reg, probe));

        var none = s.Program with { UninstallString = null, QuietUninstallString = null };
        Assert.Contains("kayıtlı değil", ForceUninstall.BrokenReason(none, s.Reg, probe));

        Assert.Null(ForceUninstall.BrokenReason(s.Program with { NoRemove = true }, s.Reg, probe));
    }

    [Fact]
    public void EnumerationMarksMissingUninstaller()
    {
        using var s = new Setup();
        var list = InstalledPrograms.EnumerateRegistry(s.Reg, new FakeProbe(), new EnumerateOptions { IncludeMsix = false, MeasureSize = false, DetectBySignature = false });
        Assert.True(Assert.Single(list, p => p.DisplayName == "Zqxv Widget").UninstallerMissing);
    }
}
