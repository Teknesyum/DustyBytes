using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;

namespace DustyBytes.Safety.Tests;

public class DeleteTests
{
    [Fact]
    public void DirectDelete_Junction_Hedefine_Girmez()
    {
        using var tree = new TempTree();
        var secret = tree.File(@"disarida\gizli.txt", "dokunma");
        tree.File(@"onbellek\alt\parca.bin", "12345");
        var ro = tree.File(@"onbellek\salt-okunur.txt", "ro");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        TempTree.Junction(tree.P("onbellek", "baglanti"), tree.P("disarida"));

        var result = new DirectDelete(SafetyGate.LoadDefault()).Delete(tree.P("onbellek"));

        Assert.True(result.Status == OpStatus.Done, result.Message);
        Assert.False(Directory.Exists(tree.P("onbellek")));
        Assert.True(File.Exists(secret));
        Assert.Equal("dokunma", File.ReadAllText(secret));
        Assert.Equal(7, result.Bytes);
    }

    [Fact]
    public void DirectDelete_Kilitli_Dosyayi_Atlar_Ve_Listeler()
    {
        using var tree = new TempTree();
        tree.File(@"cache\a.tmp", "a");
        var locked = tree.File(@"cache\kilitli.tmp", "k");
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = new DirectDelete(SafetyGate.LoadDefault()).Delete(tree.P("cache"));
            Assert.Equal(OpStatus.Locked, result.Status);
            Assert.Contains(locked, result.Skipped, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(result.Holders, h => h.Pid == Environment.ProcessId);
        }
        Assert.False(File.Exists(tree.P("cache", "a.tmp")));
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void DirectDelete_Korumali_Adi_Atlar()
    {
        using var tree = new TempTree();
        tree.File(@"artik\x.o", "x");
        var git = tree.File(@"artik\.git\HEAD", "ref");
        var result = new DirectDelete(SafetyGate.LoadDefault()).Delete(tree.P("artik"));
        Assert.NotEqual(OpStatus.Done, result.Status);
        Assert.True(File.Exists(git));
        Assert.False(File.Exists(tree.P("artik", "x.o")));
    }

    [Fact]
    public void LockInfo_Kendi_Surecimizi_Bulur()
    {
        using var tree = new TempTree();
        var file = tree.File("kilit.dat", "x");
        Assert.Empty(LockInfo.Holders(file));
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var holders = LockInfo.Holders(file);
            var me = Assert.Single(holders, h => h.Pid == Environment.ProcessId);
            Assert.False(string.IsNullOrWhiteSpace(me.Name));
        }
    }

    [Fact]
    public void Prova_Kipi_Hicbir_Dosyaya_Dokunmaz()
    {
        using var tree = new TempTree();
        var file = tree.File(@"kaynak\dosya.txt", "prova", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var dir = tree.P("kaynak");
        tree.File(@"kaynak\alt\b.txt", "b");
        var gate = SafetyGate.LoadDefault();
        var store = new QuarantineStore(gate, new QuarantineOptions { RootResolver = _ => tree.P(".dustybytes", "quarantine"), FallbackToRecycleBin = false });

        var real = store.Quarantine(tree.File(@"ayri\gercek.txt", "g"));
        Assert.True(real.Ok, real.Message);

        var before = Snapshot(tree.Root);
        var log = tree.P("log", "dryrun.log");
        var previousLog = DryRunLog.FilePath;
        var previous = Environment.GetEnvironmentVariable(DryRun.Variable);
        Environment.SetEnvironmentVariable(DryRun.Variable, "1");
        DryRunLog.FilePath = log;
        try
        {
            Assert.True(DryRun.Enabled);
            var results = new List<OpResult>
            {
                store.Quarantine(file),
                new DirectDelete(gate).Delete(dir),
                new RecycleBin(gate).Send(file),
                store.Restore(real.Id!),
                store.Purge(real.Id!),
                LockInfo.ScheduleDeleteOnReboot(file),
            };
            results.AddRange(store.PurgeExpired(DateTime.UtcNow.AddYears(1)));

            Assert.All(results, r => Assert.Equal(OpStatus.DryRun, r.Status));
            Assert.All(results, r => Assert.True(r.IsDryRun));
            Assert.Equal(7, results.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DryRun.Variable, previous);
            DryRunLog.FilePath = previousLog;
        }

        var lines = File.ReadAllLines(log);
        Assert.Equal(7, lines.Length);
        Directory.Delete(tree.P("log"), true);
        Assert.Equal(before, Snapshot(tree.Root));
        Assert.Equal(QuarantineState.Pending, Assert.Single(store.List()).State);
    }

    static string Snapshot(string root) =>
        string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("manifest.db", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}|{File.GetAttributes(p)}"
                : $"{p}|dir|{File.GetAttributes(p)}"));
}
