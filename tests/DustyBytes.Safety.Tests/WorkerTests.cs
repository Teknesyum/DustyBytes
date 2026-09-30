using System.Text.Json;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Ipc;
using DustyBytes.Worker;

namespace DustyBytes.Safety.Tests;

public class WorkerTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    sealed class Harness : IAsyncDisposable
    {
        public TempTree Tree { get; } = new();
        public WorkerServer Server { get; }
        public Task Running { get; }
        public string Pipe { get; } = WorkerClient.NewPipeName();

        public Harness(int? parent = null)
        {
            var services = new WorkerServices(SafetyGate.LoadDefault(), new QuarantineOptions
            {
                RootResolver = _ => Tree.P(".dustybytes", "quarantine"),
                FallbackToRecycleBin = false,
            });
            Server = new WorkerServer(Pipe, parent ?? Environment.ProcessId, services, watchParent: false);
            Running = Server.RunAsync();
        }

        public Task<WorkerClient> Connect() => WorkerClient.ConnectAsync(Pipe, Environment.ProcessId, Timeout);

        public async ValueTask DisposeAsync()
        {
            Server.Stop();
            await Running.WaitAsync(Timeout);
            Tree.Dispose();
        }
    }

    sealed class Collect : IProgress<WorkerProgress>
    {
        public List<WorkerProgress> Items { get; } = [];
        public void Report(WorkerProgress value) => Items.Add(value);
    }

    [Fact]
    public async Task Ping_Ve_Onaysiz_Yikici_Islem_Reddi()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();

        var ping = await client.SendAsync(new WorkerRequest { Op = Ops.Ping });
        Assert.True(ping.Ok);
        Assert.Equal("pong", ping.Message);
        Assert.Equal(Environment.ProcessId.ToString(), ping.Payload);

        var file = h.Tree.File(@"kaynak\a.txt", "veri");
        foreach (var op in new[] { Ops.Quarantine, Ops.Delete, Ops.Purge, Ops.Uninstall, Ops.RemoveLeftovers, Ops.Clean, Ops.SystemClean, Ops.Compress, Ops.Uncompress, Ops.CloudFree, Ops.CloudKeep })
        {
            var denied = await client.SendAsync(new WorkerRequest { Op = op, Paths = [file], Items = [Guid.NewGuid().ToString("N")] });
            Assert.False(denied.Ok);
            Assert.Contains("onay", denied.Message);
        }
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Onayli_Karantina_Liste_Geri_Yukleme()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var file = h.Tree.File(@"kaynak\b.bin", "12345");
        var progress = new Collect();

        var moved = await client.SendAsync(new WorkerRequest { Op = Ops.Quarantine, Paths = [file], UserApproved = true, UnitId = "u1" }, progress);
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(5, moved.PendingBytes);
        Assert.NotEmpty(progress.Items);
        Assert.False(File.Exists(file));

        var list = await client.SendAsync(new WorkerRequest { Op = Ops.ListQuarantine });
        var listing = JsonSerializer.Deserialize(list.Payload!, WorkerJson.Default.QuarantineListing)!;
        var entry = Assert.Single(listing.Entries);
        Assert.Equal("u1", entry.UnitId);
        Assert.Equal(5, list.PendingBytes);

        var restored = await client.SendAsync(new WorkerRequest { Op = Ops.Restore, Items = [entry.Id] });
        Assert.True(restored.Ok, restored.Message);
        Assert.True(File.Exists(file));

        var bad = await client.SendAsync(new WorkerRequest { Op = Ops.Quarantine, Paths = [Environment.ExpandEnvironmentVariables(@"%WINDIR%\win.ini")], UserApproved = true });
        Assert.False(bad.Ok);
        Assert.StartsWith("Denied", Assert.Single(bad.Items).Message);
    }

    [Fact]
    public async Task LockInfo_Ve_Kayitli_Isleyici()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var file = h.Tree.File("kilit.dat", "x");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var info = await client.SendAsync(new WorkerRequest { Op = Ops.LockInfo, Paths = [file] });
            var holders = JsonSerializer.Deserialize(info.Payload!, WorkerJson.Default.ListLockHolder)!;
            Assert.Contains(holders, x => x.Pid == Environment.ProcessId);
        }

        WorkerHandlers.Register(Ops.FastScan, (req, progress, ct) =>
        {
            progress.Report(new WorkerProgress(req.Id, "tarama", 50, null));
            return Task.FromResult(new WorkerResponse { Id = req.Id, Ok = true, Message = "tarandı" });
        });
        try
        {
            var progress = new Collect();
            var scan = await client.SendAsync(new WorkerRequest { Op = Ops.FastScan }, progress);
            Assert.True(scan.Ok);
            Assert.Equal("tarandı", scan.Message);
            Assert.Single(progress.Items);

            var missing = await client.SendAsync(new WorkerRequest { Op = Ops.Uninstall, UserApproved = true });
            Assert.False(missing.Ok);
            Assert.Contains("işleyici yok", missing.Message);
            Assert.Throws<InvalidOperationException>(() => WorkerHandlers.Register(Ops.Delete, (r, p, c) => Task.FromResult(new WorkerResponse { Id = r.Id })));
        }
        finally
        {
            WorkerHandlers.Unregister(Ops.FastScan);
        }
    }

    [Fact]
    public async Task Yanlis_Ebeveyn_Pid_Baglantisi_Kesilir()
    {
        var rejected = new TaskCompletionSource<int>();
        await using var h = new Harness(parent: 999_999);
        h.Server.RejectedClient += pid => rejected.TrySetResult(pid);
        await using var client = await h.Connect();
        Assert.Equal(Environment.ProcessId, await rejected.Task.WaitAsync(Timeout));
        await Assert.ThrowsAnyAsync<IOException>(() => client.SendAsync(new WorkerRequest { Op = Ops.Ping }).WaitAsync(Timeout));
    }

    [Fact]
    public async Task Shutdown_Sunucuyu_Kapatir()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var bye = await client.SendAsync(new WorkerRequest { Op = Ops.Shutdown });
        Assert.True(bye.Ok);
        await h.Running.WaitAsync(Timeout);
    }

    [Fact]
    public void Host_Eksik_Argumanla_Kullanim_Hatasi()
    {
        Assert.Equal(WorkerHost.ExitUsage, WorkerHost.Run(["--worker"]));
        Assert.Equal(WorkerHost.ExitUsage, WorkerHost.Run(["--pipe", "a b", "--parent", "1"]));
        Assert.Equal(WorkerHost.ExitNoParent, WorkerHost.Run(["--pipe", "x", "--parent", "999999"]));
    }

    [Fact]
    public async Task Tek_Ornek_Ikinciyi_Ilkine_Yonlendirir()
    {
        var app = "DustyBytesTest" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.Acquire(app);
        Assert.True(first.IsFirst);
        var got = new TaskCompletionSource<string[]>();
        first.Activated += (_, args) => got.TrySetResult(args);
        first.StartListening();

        using var second = SingleInstance.Acquire(app);
        Assert.False(second.IsFirst);
        Assert.True(await second.NotifyFirstAsync(["--ac", "C:\\Oyunlar"]));
        Assert.Equal(["--ac", "C:\\Oyunlar"], await got.Task.WaitAsync(Timeout));
    }
}
