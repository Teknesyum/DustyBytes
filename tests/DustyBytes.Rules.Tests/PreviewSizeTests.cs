using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.SystemCleanup;
using DustyBytes.Core.Ipc;
using Xunit.Abstractions;

namespace DustyBytes.Rules.Tests;

public class PreviewSizeTests(ITestOutputHelper output)
{
    const int Paths = 2_000_000;
    const int MessageLimit = 1 << 20;

    static int WireBytes(WorkerRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request, IpcJson.Default.WorkerRequest));

    static int WireBytes(WorkerResponse response) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(response, IpcJson.Default.WorkerResponse));

    [Fact]
    public void Two_Million_Path_Preview_Stays_Under_One_Megabyte_On_The_Wire()
    {
        var watch = Stopwatch.StartNew();
        var builder = new PreviewBuilder();
        var stamp = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < Paths; i++)
            builder.Add($@"C:\Users\kullanici\AppData\Local\Temp\{i % 977}\dosya-{i}.tmp", 4096, stamp, i % 3 == 0 ? "windows/temp" : "chrome/cache");
        var built = builder.Build();
        var builtMs = watch.ElapsedMilliseconds;

        var store = new PreviewStore();
        var request = new WorkerRequest { Op = Ops.CleanPreview, Items = ["chrome/cache", "windows/temp"] };
        var response = WorkerCleanHandlers.PreviewResponse(request, built, store);
        var preview = JsonSerializer.Deserialize(response.Payload!, IpcJson.Default.CleanPreview)!;

        var clean = new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = request.Items, PreviewId = preview.Id, Digest = preview.Digest };
        var responseBytes = WireBytes(response);
        var cleanBytes = WireBytes(clean);
        output.WriteLine($"{Paths:N0} yol: kurulum {builtMs} ms, önizleme yanıtı {responseBytes:N0} bayt, temizlik isteği {cleanBytes:N0} bayt");

        Assert.Equal(Paths, preview.Count);
        Assert.Equal(Paths * 4096L, preview.Bytes);
        Assert.Equal(CleanPreview.HeadLimit, preview.Head.Count);
        Assert.Equal(2, preview.Options.Count);
        Assert.Equal(Paths, preview.Options.Sum(o => o.Count));
        Assert.True(responseBytes < MessageLimit, $"Önizleme yanıtı {responseBytes:N0} bayt");
        Assert.True(cleanBytes < MessageLimit, $"Temizlik isteği {cleanBytes:N0} bayt");

        var shown = store.Take(preview.Id, preview.Digest, clean.Items);
        Assert.NotNull(shown);
        Assert.Equal(Paths, shown.Count);
        Assert.True(shown.Contains($@"C:\Users\kullanici\AppData\Local\Temp\{1999998 % 977}\dosya-1999998.tmp"));
        Assert.False(shown.Contains(@"C:\Users\kullanici\AppData\Local\Temp\0\dosya-2000000.tmp"));
    }

    [Fact]
    public void Head_Is_Capped_By_Characters_For_Very_Long_Paths()
    {
        var builder = new PreviewBuilder();
        var tail = new string('a', 30_000);
        for (var i = 0; i < 300; i++)
            builder.Add($@"\\?\C:\uzun\{i}\{tail}", 1, default, "x/y");

        var preview = builder.Build().Preview;
        var bytes = WireBytes(new WorkerResponse { Id = "x", Ok = true, Payload = JsonSerializer.Serialize(preview, IpcJson.Default.CleanPreview) });

        Assert.Equal(300, preview.Count);
        Assert.True(preview.Head.Sum(f => f.Path.Length) <= CleanPreview.HeadChars);
        Assert.True(bytes < MessageLimit, $"{bytes:N0} bayt");
    }

    [Fact]
    public void Store_Keeps_Only_A_Few_Recent_Previews()
    {
        var store = new PreviewStore();
        var ids = new List<CleanPreview>();
        for (var i = 0; i < PreviewStore.Capacity + 2; i++)
        {
            var builder = new PreviewBuilder();
            builder.Add($@"C:\t\{i}.tmp", 1, default, "a/b");
            ids.Add(store.Add(builder.Build(), ["a/b"]));
        }

        Assert.Equal(PreviewStore.Capacity, store.Count);
        Assert.Null(store.Take(ids[0].Id, ids[0].Digest, ["a/b"]));
        Assert.NotNull(store.Take(ids[^1].Id, ids[^1].Digest, ["a/b"]));
    }
}
