using System.Diagnostics;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Ipc;
using DustyBytes.Worker;

namespace DustyBytes.Safety.Tests;

public class DuplicateWorkerTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    const string UnitId = "Duplicate-0123456789ABCDEF";

    sealed class Harness : IAsyncDisposable
    {
        public TempTree Tree { get; } = new();
        public WorkerServer Server { get; }
        public Task Running { get; }
        public string Pipe { get; } = WorkerClient.NewPipeName();

        public Harness()
        {
            var services = new WorkerServices(SafetyGate.LoadDefault(), new QuarantineOptions
            {
                RootResolver = _ => Tree.P(".dustybytes", "quarantine"),
                FallbackToRecycleBin = false,
            });
            Server = new WorkerServer(Pipe, Environment.ProcessId, services, watchParent: false);
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

    static string Content(int seed) => string.Concat(Enumerable.Range(0, 3000).Select(i => (char)('a' + (i * 7 + seed) % 26)));

    static WorkerRequest Request(string keep, params string[] paths) => new()
    {
        Op = Ops.Quarantine,
        Paths = [.. paths],
        UnitId = UnitId,
        Target = keep,
        UserApproved = true,
        IncludeUserData = true,
    };

    [Fact]
    public async Task Ayni_Icerik_Karantinaya_Alinir_Kalacak_Yerinde_Kalir()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));

        var response = await client.SendAsync(Request(keep, copy));

        Assert.True(response.Ok, response.Message);
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public async Task Kalacak_Kopya_Yoksa_Reddeder()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));
        File.Delete(keep);

        var response = await client.SendAsync(Request(keep, copy));

        Assert.False(response.Ok);
        Assert.Contains("Kalacak kopya bulunamadı", response.Message);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task Kalacak_Kopya_Degistiyse_Reddeder()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));
        File.WriteAllText(keep, Content(2));

        var response = await client.SendAsync(Request(keep, copy));

        Assert.False(response.Ok);
        Assert.Contains("içeriği artık aynı değil", response.Message);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task Boyut_Degistiyse_Reddeder()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));
        File.AppendAllText(keep, "ek");

        var response = await client.SendAsync(Request(keep, copy));

        Assert.False(response.Ok);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task Sabit_Baglanti_Kopya_Sayilmaz()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var link = h.Tree.P(@"belgeler\film-baglanti.mkv");
        HardLink(link, keep);

        var response = await client.SendAsync(Request(keep, link));

        Assert.False(response.Ok);
        Assert.Contains("sabit bağlantı", response.Message);
        Assert.True(File.Exists(link));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public async Task Kalacak_Belirtilmezse_Ya_Da_Kendisi_Istenirse_Reddeder()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));

        var none = await client.SendAsync(Request(keep, copy) with { Target = null });
        var self = await client.SendAsync(Request(keep, keep));

        Assert.False(none.Ok);
        Assert.False(self.Ok);
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task Kopyalar_Kalici_Silinmez()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var keep = h.Tree.File(@"belgeler\film.mkv", Content(1));
        var copy = h.Tree.File(@"yedek\film.mkv", Content(1));

        var response = await client.SendAsync(Request(keep, copy) with { Op = Ops.Delete });

        Assert.False(response.Ok);
        Assert.Contains("yalnız karantinaya", response.Message);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task Korunan_Yol_Guvenlik_Kapisindan_Gecmez()
    {
        await using var h = new Harness();
        await using var client = await h.Connect();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var notepad = Path.Combine(windows, "notepad.exe");
        if (!File.Exists(notepad))
            return;
        var keep = h.Tree.P("notepad.exe");
        File.Copy(notepad, keep);

        var response = await client.SendAsync(Request(keep, notepad));

        Assert.False(response.Ok);
        Assert.True(File.Exists(notepad));
    }

    static void HardLink(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (!File.Exists(link))
            throw new InvalidOperationException("Sabit bağlantı oluşturulamadı: " + p.StandardError.ReadToEnd());
    }
}
