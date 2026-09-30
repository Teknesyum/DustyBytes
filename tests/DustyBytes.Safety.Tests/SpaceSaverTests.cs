using DustyBytes.Clean.Safety;
using DustyBytes.Clean.SpaceSaver;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;
using DustyBytes.Worker;

namespace DustyBytes.Safety.Tests;

public class SpaceSaverTests
{
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    const uint CloudTag = 0x9000601A;
    const uint SymlinkTag = 0xA000000C;

    sealed class Collect : IProgress<WorkerProgress>
    {
        public List<WorkerProgress> Items { get; } = [];
        public void Report(WorkerProgress value) => Items.Add(value);
    }

    sealed class CloudSetup
    {
        public required SafetyGate Gate { get; init; }
        public required string SyncRoot { get; init; }
        public Dictionary<string, (FileAttributes Attrs, uint? Tag)> Fake { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    static CloudSetup Cloud(TempTree tree)
    {
        var sync = tree.Dir("OneDrive");
        var list = ProtectedList.LoadDefault();
        list.AddRoot(sync, "Bulut, senkron: OneDrive", Badge.Cloud);
        list.AddSyncRoot(sync);
        var setup = new CloudSetup { Gate = new SafetyGate(list), SyncRoot = sync };
        list.AttributeProvider = p => setup.Fake.TryGetValue(p, out var f) ? f.Attrs : ProtectedList.DefaultAttributes(p);
        setup.Gate.ReparseTagProvider = p => setup.Fake.TryGetValue(p, out var f) ? f.Tag : null;
        return setup;
    }

    [Fact]
    public void Compress_Gate_Keeps_System_And_Links_Out()
    {
        using var tree = new TempTree();
        var gate = SafetyGate.LoadDefault();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.False(gate.Check(windows, GateOp.Compress, false).Allowed);
        Assert.False(gate.Check(Path.Combine(windows, "System32"), GateOp.Compress, false).Allowed);
        Assert.False(gate.Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"), GateOp.Compress, false).Allowed);
        Assert.False(gate.Check(tree.Dir(@".dustybytes\quarantine"), GateOp.Compress, false).Allowed);

        var target = tree.Dir("hedef");
        var link = tree.P("baglanti");
        TempTree.Junction(link, target);
        Assert.Equal(Badge.Link, gate.Check(link, GateOp.Compress, false).Badge);
        Assert.True(gate.Check(tree.Dir("Oyun"), GateOp.Compress, false).Allowed);
    }

    [Fact]
    public void Compress_Gate_Enters_Launcher_Library_But_Not_Its_Root()
    {
        using var tree = new TempTree();
        var gate = SafetyGate.LoadDefault();
        var library = tree.Dir("SteamLibrary");
        var game = tree.Dir(@"SteamLibrary\steamapps\common\Oyun");
        gate.List.AddLauncherLibrary(library, "Steam");

        Assert.True(gate.Check(game, GateOp.Compress, false).Allowed);
        Assert.False(gate.Check(game, GateOp.Remove, false).Allowed);
        Assert.False(gate.Check(library, GateOp.Compress, false).Allowed);
    }

    [Fact]
    public void Cloud_Gate_Allows_Only_Hydrated_Files_Inside_Sync_Root()
    {
        using var tree = new TempTree();
        var c = Cloud(tree);
        var file = tree.File(@"OneDrive\Arsiv\eski.pdf");
        var folder = Path.GetDirectoryName(file)!;
        c.Fake[file] = (FileAttributes.Archive | FileAttributes.ReparsePoint, CloudTag);
        c.Fake[folder] = (FileAttributes.Directory | FileAttributes.ReparsePoint, CloudTag);

        Assert.True(c.Gate.Check(file, GateOp.CloudFree, true).Allowed);
        Assert.False(c.Gate.Check(file, GateOp.CloudFree, false).Allowed);
        Assert.False(c.Gate.Check(file, GateOp.Remove, true).Allowed);
        Assert.False(c.Gate.Check(c.SyncRoot, GateOp.CloudFree, true).Allowed);
        Assert.False(c.Gate.Check(folder, GateOp.CloudFree, true).Allowed);

        var outside = tree.File("disari.pdf");
        c.Fake[outside] = (FileAttributes.Archive | FileAttributes.ReparsePoint, CloudTag);
        Assert.False(c.Gate.Check(outside, GateOp.CloudFree, true).Allowed);

        var windowsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.False(c.Gate.Check(windowsFile, GateOp.CloudFree, true).Allowed);
    }

    [Fact]
    public void Cloud_Gate_Refuses_Dehydrated_Plain_And_Linked_Files()
    {
        using var tree = new TempTree();
        var c = Cloud(tree);
        var online = tree.File(@"OneDrive\cevrimici.pdf");
        c.Fake[online] = (FileAttributes.ReparsePoint | RecallOnDataAccess | FileAttributes.Offline, CloudTag);
        Assert.False(c.Gate.Check(online, GateOp.CloudFree, true).Allowed);
        Assert.True(c.Gate.Check(online, GateOp.CloudKeep, true).Allowed);

        var plain = tree.File(@"OneDrive\duz.pdf");
        Assert.False(c.Gate.Check(plain, GateOp.CloudFree, true).Allowed);

        var linked = tree.File(@"OneDrive\bag.pdf");
        c.Fake[linked] = (FileAttributes.ReparsePoint, SymlinkTag);
        Assert.Equal(Badge.Link, c.Gate.Check(linked, GateOp.CloudFree, true).Badge);

        var viaLink = tree.File(@"OneDrive\Baglanti\ic.pdf");
        c.Fake[viaLink] = (FileAttributes.ReparsePoint, CloudTag);
        c.Fake[Path.GetDirectoryName(viaLink)!] = (FileAttributes.Directory | FileAttributes.ReparsePoint, SymlinkTag);
        Assert.Equal(Badge.Link, c.Gate.Check(viaLink, GateOp.CloudFree, true).Badge);
    }

    [Theory]
    [InlineData("oyun.dll", 200_000, FileAttributes.Archive, true)]
    [InlineData("oyun.dll", 10_000, FileAttributes.Archive, false)]
    [InlineData("film.MKV", 200_000_000, FileAttributes.Archive, false)]
    [InlineData("yedek.7z", 200_000_000, FileAttributes.Archive, false)]
    [InlineData("resim.jpg", 5_000_000, FileAttributes.Archive, false)]
    [InlineData("veri.pak", 200_000_000, FileAttributes.Compressed, false)]
    [InlineData("veri.pak", 200_000_000, FileAttributes.ReparsePoint, false)]
    [InlineData("veri.pak", 200_000_000, FileAttributes.Encrypted, false)]
    [InlineData("veri.pak", 200_000_000, FileAttributes.Normal, true)]
    public void Filter_Skips_Tiny_Media_Archives_And_Special_Files(string name, long length, FileAttributes attrs, bool accepted) =>
        Assert.Equal(accepted, CompressFilter.Accepts(name, length, attrs));

    [Fact]
    public void Plan_Walks_Unit_And_Honours_Filters_And_Checks()
    {
        using var tree = new TempTree();
        var root = tree.Dir("Oyun");
        var big = Big(tree, @"Oyun\veri.bin");
        Big(tree, @"Oyun\ara.mkv");
        Big(tree, @"Oyun\Cache\gecici.bin");
        Big(tree, @"Oyun\gizli\kayit.bin");
        tree.File(@"Oyun\kucuk.bin", "az");

        var files = CompressPlan.Candidates([root, root], p => p.Contains("gizli", StringComparison.OrdinalIgnoreCase) ? Verdict.Deny("gizli", Badge.System) : Verdict.Ok);

        var only = Assert.Single(files);
        Assert.Equal(big, only.Path);
        Assert.Equal(128 * 1024, only.Length);
        Assert.Empty(CompressPlan.Candidates([root], _ => Verdict.Deny("hayır", Badge.System)));
    }

    static string Big(TempTree tree, string relative) => tree.File(relative, new string('a', 128 * 1024));

    [Fact]
    public void Estimator_Is_Conservative_And_Cluster_Aware()
    {
        var files = Enumerable.Range(0, 10).Select(i => new PlannedFile($@"C:\oyun\{i}.bin", 100L << 20)).ToList();
        var eligible = files.Sum(f => f.Length);

        var zeros = CompressEstimator.Estimate(files, _ => [new byte[8192], new byte[8192]]);
        Assert.Equal(eligible, zeros.EligibleBytes);
        Assert.Equal((long)(eligible * 0.5 * CompressEstimator.Margin), zeros.GainBytes);
        Assert.Equal(10, zeros.Sampled);

        var random = new Random(7);
        var noisy = CompressEstimator.Estimate(files, _ =>
        {
            var chunk = new byte[8192];
            random.NextBytes(chunk);
            return [chunk];
        });
        Assert.Equal(0, noisy.GainBytes);
        Assert.Equal(0, CompressEstimator.Estimate([], _ => []).GainBytes);
    }

    [Fact]
    public void Estimator_Samples_Largest_And_Spread()
    {
        var files = Enumerable.Range(1, 500).Select(i => new PlannedFile($@"C:\oyun\{i}.bin", i * 1000L)).ToList();
        var sample = CompressEstimator.Sample(files);

        Assert.Equal(CompressEstimator.MaxSamples, sample.Count);
        Assert.Contains(sample, f => f.Length == 500_000);
        Assert.Contains(sample, f => f.Length < 100_000);
        Assert.Equal(sample.Count, sample.Distinct().Count());
    }

    [Fact]
    public void Cloud_Eligibility_Needs_Full_Sync()
    {
        var hydrated = FileAttributes.Archive | FileAttributes.ReparsePoint;
        var ok = new CloudState(true, 0, 5000, 0);

        Assert.True(CloudFiles.IsEligible(ok, 5000, hydrated));
        Assert.True(CloudFiles.IsEligible(ok with { PinState = 4 }, 5000, hydrated));
        Assert.False(CloudFiles.IsEligible(ok with { InSync = false }, 5000, hydrated));
        Assert.False(CloudFiles.IsEligible(ok with { ModifiedBytes = 10 }, 5000, hydrated));
        Assert.False(CloudFiles.IsEligible(ok with { OnDiskBytes = 100 }, 5000, hydrated));
        Assert.False(CloudFiles.IsEligible(ok with { PinState = 1 }, 5000, hydrated));
        Assert.False(CloudFiles.IsEligible(ok, 5000, hydrated | CloudFiles.Pinned));
        Assert.False(CloudFiles.IsEligible(ok, 5000, hydrated | RecallOnDataAccess));
        Assert.False(CloudFiles.IsEligible(ok, 5000, FileAttributes.Archive));
        Assert.False(CloudFiles.IsEligible(null, 5000, hydrated));

        Assert.True(CloudFiles.IsCloudTag(0x9000001A));
        Assert.True(CloudFiles.IsCloudTag(CloudTag));
        Assert.False(CloudFiles.IsCloudTag(SymlinkTag));
        Assert.False(CloudFiles.IsCloudTag(null));

        var now = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(CloudFiles.IsOldEnough(now.AddDays(-400), now.AddDays(-200), now));
        Assert.False(CloudFiles.IsOldEnough(now.AddDays(-400), now.AddDays(-3), now));
    }

    [Fact]
    public async Task Compress_Handler_Refuses_System_Running_And_Non_Ntfs()
    {
        using var tree = new TempTree();
        var game = tree.Dir("Oyun");
        Big(tree, @"Oyun\veri.bin");
        var progress = new Collect();

        var blocked = await new SpaceHandlers(SafetyGate.LoadDefault()).HandleCompress(
            new WorkerRequest { Op = Ops.Compress, Paths = [Environment.GetFolderPath(Environment.SpecialFolder.Windows)], UserApproved = true }, progress, default);
        Assert.False(blocked.Ok);

        var running = await new SpaceHandlers(SafetyGate.LoadDefault()) { Running = _ => ["oyun"], Unsupported = _ => null }.HandleCompress(
            new WorkerRequest { Op = Ops.Compress, Paths = [game], UserApproved = true }, progress, default);
        Assert.False(running.Ok);
        Assert.Contains("oyun", running.Message);

        var fat = await new SpaceHandlers(SafetyGate.LoadDefault()) { Running = _ => [], Unsupported = _ => "NTFS değil" }.HandleCompress(
            new WorkerRequest { Op = Ops.Compress, Paths = [game], UserApproved = true }, progress, default);
        Assert.False(fat.Ok);
        Assert.Equal("NTFS değil", fat.Message);
    }

    [Fact]
    public async Task Compress_And_Undo_Round_Trip_On_Ntfs()
    {
        using var tree = new TempTree();
        var game = tree.Dir("Oyun");
        if (WofCompressor.Unsupported(game) is not null)
            return;
        var file = tree.File(@"Oyun\veri.bin", string.Concat(Enumerable.Repeat("DustyBytes küçültme denemesi ", 40_000)));
        var original = await File.ReadAllBytesAsync(file);
        var handlers = new SpaceHandlers(SafetyGate.LoadDefault()) { Running = _ => [] };
        var progress = new Collect();

        var packed = await handlers.HandleCompress(new WorkerRequest { Op = Ops.Compress, Paths = [game], UserApproved = true }, progress, default);
        Assert.True(packed.Ok, packed.Message);
        Assert.True(packed.FreedBytes > 0, packed.Message);
        Assert.True(WofCompressor.IsCompressed(file));
        Assert.NotEmpty(progress.Items);
        Assert.Equal(original, await File.ReadAllBytesAsync(file));

        var restored = await handlers.HandleUncompress(new WorkerRequest { Op = Ops.Uncompress, Paths = [game], UserApproved = true }, progress, default);
        Assert.True(restored.Ok, restored.Message);
        Assert.True(restored.PendingBytes > 0, restored.Message);
        Assert.False(WofCompressor.IsCompressed(file));
        Assert.Equal(original, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task Cloud_Handlers_Recheck_Sync_And_Age_Before_Unpinning()
    {
        using var tree = new TempTree();
        var c = Cloud(tree);
        var old = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ready = tree.File(@"OneDrive\hazir.pdf", "veri", old);
        var recent = tree.File(@"OneDrive\yeni.pdf", "veri");
        var dirty = tree.File(@"OneDrive\kirli.pdf", "veri", old);
        var outside = tree.File("disari.pdf", "veri", old);
        foreach (var f in new[] { ready, recent, dirty, outside })
            c.Fake[f] = (FileAttributes.Archive | FileAttributes.ReparsePoint, CloudTag);

        var pins = new List<(string, bool)>();
        var handlers = new SpaceHandlers(c.Gate)
        {
            Probe = p => new CloudState(!p.EndsWith("kirli.pdf", StringComparison.Ordinal), 0, 4, 0),
            SetPin = (p, keep) =>
            {
                pins.Add((p, keep));
                return 0;
            },
            SettleTime = TimeSpan.Zero,
        };

        var freed = await handlers.HandleCloudFree(new WorkerRequest
        {
            Op = Ops.CloudFree,
            Paths = [ready, recent, dirty, outside],
            UserApproved = true,
            IncludeUserData = true,
        }, new Collect(), default);

        Assert.True(freed.Ok, freed.Message);
        Assert.Equal([(ready, false)], pins);
        Assert.Equal(3, freed.Items.Count(i => !i.Ok));

        var noConsent = await handlers.HandleCloudFree(new WorkerRequest { Op = Ops.CloudFree, Paths = [ready], UserApproved = true }, new Collect(), default);
        Assert.False(noConsent.Ok);

        pins.Clear();
        var kept = await handlers.HandleCloudKeep(new WorkerRequest
        {
            Op = Ops.CloudKeep,
            Paths = [ready, outside],
            UserApproved = true,
            IncludeUserData = true,
        }, new Collect(), default);
        Assert.True(kept.Ok, kept.Message);
        Assert.Equal([(ready, true)], pins);
    }
}
