using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;

namespace DustyBytes.Tests;

public sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
    public Exception? Throw { get; set; }
    public List<string> Hits { get; } = [];

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Hits)
            Hits.Add(url);
        if (Throw is not null)
            throw Throw;
        return Routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    public static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };
}

public sealed class UpdateFixture : IDisposable
{
    public const string ZipUrl = "https://github.com/Teknesyum/DustyBytes/releases/download/v0.2.0/DustyBytes-win-x64.zip";
    public const string ShaUrl = ZipUrl + ".sha256";

    public UpdateFixture()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dustybytes-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Target = System.IO.Path.Combine(Root, "target");
        Directory.CreateDirectory(Target);
        Zip = MakeZip();
        Sha = Convert.ToHexStringLower(SHA256.HashData(Zip));
    }

    public string Root { get; }
    public string Target { get; }
    public byte[] Zip { get; }
    public string Sha { get; }
    public FakeHttp Http { get; } = new();
    public List<ProcessStartInfo> Launched { get; } = [];
    public List<string> Notes { get; } = [];
    public List<string> Failures { get; } = [];
    public List<bool> ConfirmAsked { get; } = [];
    public bool Busy { get; set; }

    static byte[] MakeZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, body) in new[] { ("DustyBytes.exe", "new exe"), ("rules/protected.json", "{}") })
            {
                using var w = new StreamWriter(archive.CreateEntry(name).Open());
                w.Write(body);
            }
        }
        return ms.ToArray();
    }

    public void Release(string tag, string? body, bool withShaAsset = false, byte[]? zip = null)
    {
        var assets = "{\"name\":\"DustyBytes-win-x64.zip\",\"browser_download_url\":\"" + ZipUrl + "\",\"size\":" + (zip ?? Zip).Length + "}";
        if (withShaAsset)
            assets += ",{\"name\":\"DustyBytes-win-x64.zip.sha256\",\"browser_download_url\":\"" + ShaUrl + "\",\"size\":90}";
        var json = "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"body\":" +
                   "\"" + (body ?? "").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"" + ",\"assets\":[" + assets + "]}";
        Http.Routes[UpdateService.LatestUrl] = () => FakeHttp.Text(json);
        var bytes = zip ?? Zip;
        Http.Routes[ZipUrl] = () => FakeHttp.Bytes(bytes);
        Http.Routes[ShaUrl] = () => FakeHttp.Text(Sha + "  DustyBytes-win-x64.zip\n");
    }

    public UpdateService Service(Version? fake = null, bool simulate = false, long busyLimit = 0, int stepMs = 0) =>
        new(new HttpClient(Http), new Version(0, 1, 0), new UpdateOptions
        {
            Root = System.IO.Path.Combine(Root, "update"),
            TargetDir = Target,
            ExePath = System.IO.Path.Combine(Target, "DustyBytes.exe"),
            ProcessId = 4242,
            BytesPerSecond = 0,
            BusyBytesPerSecond = busyLimit,
            FakeVersion = fake,
            Simulate = simulate || fake is not null,
            SimulatedStep = TimeSpan.FromMilliseconds(stepMs),
            Launch = psi =>
            {
                Launched.Add(psi);
                return true;
            },
        });

    public UpdateViewModel Model(UpdateService service, bool accept = true) =>
        new(service, () => Busy, (_, _, _) =>
        {
            ConfirmAsked.Add(accept);
            return Task.FromResult(accept);
        }, Notes.Add, Failures.Add, a => a());

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}

public class UpdateTests
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("v0.1.0", "0.1.0", false)]
    [InlineData("v0.0.9", "0.1.0", false)]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("V0.1.1-beta.2", "0.1.0", true)]
    [InlineData("0.1.0.7", "0.1.0", false)]
    [InlineData("v2", "1.9.9", true)]
    public void Version_Comparison(string tag, string current, bool newer)
    {
        var parsed = UpdateService.ParseTag(tag);
        Assert.NotNull(parsed);
        Assert.Equal(newer, UpdateService.IsNewer(parsed!, Version.Parse(current)));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Bad_Tag_Is_Ignored(string? tag) => Assert.Null(UpdateService.ParseTag(tag));

    [Fact]
    public void Sha_Is_Found_In_Release_Notes()
    {
        var sha = new string('a', 63) + "B";
        Assert.Equal(sha.ToLowerInvariant(), UpdateService.FindSha("Unsigned build.\n\nSHA-256: `" + sha + "`"));
        Assert.Null(UpdateService.FindSha("no hash here " + new string('a', 65)));
    }

    [Fact]
    public async Task Not_Found_Keeps_Badge_Hidden()
    {
        using var f = new UpdateFixture();
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        Assert.Equal(UpdateState.None, vm.State);
        Assert.False(vm.IsVisible);
        Assert.Empty(f.Failures);
        Assert.Contains(UpdateService.LatestUrl, f.Http.Hits);
    }

    [Fact]
    public async Task Network_Error_Keeps_Badge_Hidden()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", f.Sha);
        f.Http.Throw = new HttpRequestException("offline");
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        Assert.False(vm.IsVisible);
        Assert.Empty(f.Failures);
        Assert.Empty(f.Notes);
    }

    [Fact]
    public async Task Same_Or_Older_Release_Keeps_Badge_Hidden()
    {
        using var f = new UpdateFixture();
        f.Release("v0.1.0", f.Sha);
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public async Task Sha_Mismatch_Never_Turns_Green()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", "SHA-256: " + new string('0', 64));
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        Assert.Equal(UpdateState.Available, vm.State);

        await vm.ActCommand.ExecuteAsync(null);

        Assert.Equal(UpdateState.Available, vm.State);
        Assert.False(vm.IsReady);
        Assert.Single(f.Failures);
        Assert.Empty(f.Launched);
        var dir = System.IO.Path.Combine(f.Root, "update", "0.2.0");
        Assert.False(File.Exists(System.IO.Path.Combine(dir, UpdateService.ZipName)));
        Assert.False(File.Exists(System.IO.Path.Combine(dir, UpdateService.ZipName + ".part")));
    }

    [Fact]
    public async Task Missing_Sha_Never_Turns_Green()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", "Unsigned build.");
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        await vm.DownloadAsync();
        Assert.Equal(UpdateState.Available, vm.State);
        Assert.Single(f.Failures);
    }

    [Fact]
    public async Task Sha_Asset_Is_Used_When_Notes_Have_None()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", "Unsigned build.", withShaAsset: true);
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        await vm.DownloadAsync();
        Assert.Equal(UpdateState.Ready, vm.State);
        Assert.Contains(UpdateFixture.ShaUrl, f.Http.Hits);
    }

    [Fact]
    public async Task Download_And_Install_Installs_Without_A_Second_Question()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", "SHA-256: " + f.Sha);
        var vm = f.Model(f.Service());
        var exited = false;
        vm.Exit = () => exited = true;
        await vm.CheckAsync();
        await vm.DownloadAndInstallCommand.ExecuteAsync(null);
        Assert.Empty(f.ConfirmAsked);
        Assert.Single(f.Launched);
        Assert.True(exited);
    }

    [Fact]
    public async Task Download_And_Install_Waits_While_Work_Runs()
    {
        using var f = new UpdateFixture { Busy = true };
        f.Release("v0.2.0", "SHA-256: " + f.Sha);
        var vm = f.Model(f.Service());
        await vm.CheckAsync();
        await vm.DownloadAndInstallCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.Ready, vm.State);
        Assert.Empty(f.Launched);
        Assert.Single(f.Notes);
    }

    [Fact]
    public async Task Cancel_Returns_To_Available_Without_A_Failure()
    {
        using var f = new UpdateFixture();
        var vm = f.Model(f.Service(fake: new Version(0, 2, 0), stepMs: 50));
        await vm.CheckAsync();
        vm.CancelCommand.Execute(null);
        var run = vm.DownloadAsync();
        vm.CancelCommand.Execute(null);
        await run;
        Assert.Equal(UpdateState.Available, vm.State);
        Assert.Empty(f.Failures);
    }

    [Fact]
    public async Task State_Machine_Walks_Yellow_Downloading_Green_Installing()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", "SHA-256: " + f.Sha);
        var service = f.Service();
        var vm = f.Model(service);
        var exited = false;
        vm.Exit = () => exited = true;
        var states = new List<UpdateState> { vm.State };
        var texts = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.State))
                states.Add(vm.State);
            if (e.PropertyName == nameof(UpdateViewModel.Text))
                texts.Add(vm.Text);
        };

        await vm.CheckAsync();
        Assert.Equal("Güncelleme", vm.Text);
        Assert.False(vm.IsReady);
        Assert.DoesNotContain(UpdateFixture.ZipUrl, f.Http.Hits);

        await vm.ActCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.Ready, vm.State);
        Assert.True(vm.IsReady);
        Assert.Empty(f.Launched);
        Assert.Empty(f.ConfirmAsked);
        Assert.Contains(texts, t => t.StartsWith("İniyor %", StringComparison.Ordinal));

        await vm.ActCommand.ExecuteAsync(null);
        Assert.Equal(
            [UpdateState.None, UpdateState.Available, UpdateState.Downloading, UpdateState.Ready, UpdateState.Installing],
            states);
        Assert.Equal([true], f.ConfirmAsked);
        Assert.True(exited);

        var psi = Assert.Single(f.Launched);
        var args = psi.ArgumentList.ToList();
        Assert.Equal("powershell.exe", psi.FileName);
        Assert.Equal("Hidden", args[args.IndexOf("-WindowStyle") + 1]);
        Assert.Equal("4242", args[args.IndexOf("-ProcessId") + 1]);
        Assert.Equal(f.Target, args[args.IndexOf("-Target") + 1]);
        var script = args[args.IndexOf("-File") + 1];
        Assert.Contains(".old", File.ReadAllText(script));
        var source = args[args.IndexOf("-Source") + 1];
        Assert.True(File.Exists(System.IO.Path.Combine(source, "DustyBytes.exe")));
        Assert.True(File.Exists(System.IO.Path.Combine(source, "rules", "protected.json")));
        Assert.StartsWith(System.IO.Path.Combine(f.Root, "update", "0.2.0"), source);
    }

    [Fact]
    public async Task Declined_Install_Stays_Green()
    {
        using var f = new UpdateFixture();
        f.Release("v0.2.0", f.Sha);
        var vm = f.Model(f.Service(), accept: false);
        await vm.CheckAsync();
        await vm.DownloadAsync();
        await vm.InstallAsync();
        Assert.Equal(UpdateState.Ready, vm.State);
        Assert.Empty(f.Launched);
    }

    [Fact]
    public async Task Dry_Run_Fakes_Every_Step_Without_Network()
    {
        using var f = new UpdateFixture();
        var vm = f.Model(f.Service(fake: new Version(9, 9, 9)));
        var states = new List<UpdateState>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.State))
                states.Add(vm.State);
        };
        await vm.CheckAsync();
        Assert.Equal("9.9.9", vm.Info!.Version.ToString(3));
        await vm.DownloadAsync();
        await vm.InstallAsync();
        Assert.Equal([UpdateState.Available, UpdateState.Downloading, UpdateState.Ready, UpdateState.Installing, UpdateState.Ready], states);
        Assert.Empty(f.Http.Hits);
        Assert.Empty(f.Launched);
        Assert.Contains(f.Notes, n => n.StartsWith("Prova kipi", StringComparison.Ordinal));
        Assert.False(Directory.Exists(System.IO.Path.Combine(f.Root, "update")));
    }

    [Fact]
    public async Task Download_Runs_Below_Normal_And_Slows_When_Busy()
    {
        using var f = new UpdateFixture();
        var big = new byte[256 * 1024];
        Random.Shared.NextBytes(big);
        f.Release("v0.2.0", Convert.ToHexStringLower(SHA256.HashData(big)), zip: big);
        f.Busy = true;
        var service = f.Service(busyLimit: 512 * 1024);
        var info = await service.CheckAsync();
        Assert.NotNull(info);
        var priorities = new List<ThreadPriority>();
        var clock = Stopwatch.StartNew();
        var result = await service.DownloadAsync(info!, _ => priorities.Add(Thread.CurrentThread.Priority), () => f.Busy);
        clock.Stop();
        Assert.True(result.Ok, result.Error);
        Assert.All(priorities, p => Assert.Equal(ThreadPriority.BelowNormal, p));
        Assert.True(clock.ElapsedMilliseconds >= 350, clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Cleanup_Removes_Only_Replaced_Old_Files()
    {
        using var f = new UpdateFixture();
        File.WriteAllText(System.IO.Path.Combine(f.Target, "a.dll"), "new");
        File.WriteAllText(System.IO.Path.Combine(f.Target, "a.dll.old"), "old");
        File.WriteAllText(System.IO.Path.Combine(f.Target, "lonely.old"), "keep");
        Assert.Equal(1, UpdateService.CleanupOld(f.Target));
        Assert.False(File.Exists(System.IO.Path.Combine(f.Target, "a.dll.old")));
        Assert.True(File.Exists(System.IO.Path.Combine(f.Target, "lonely.old")));
    }
}

public class UpdateBadgeTests
{
    static void Pump()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Output()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !d.EnumerateFiles("*.slnx").Any())
            d = d.Parent;
        var dir = System.IO.Path.Combine(d!.FullName, "tmp", "uc");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static DustyBytes.App.Kabuk.TitleBar Bar(Window window) => window.FindControl<DustyBytes.App.Kabuk.TitleBar>("TitleBar")!;

    [AvaloniaTheory]
    [InlineData("var", UpdateState.Available, "IndirDugmesi,IndirVeYukleDugmesi")]
    [InlineData("iniyor", UpdateState.Downloading, "IptalDugmesi")]
    [InlineData("hazir", UpdateState.Ready, "YukleDugmesi")]
    public void Badge_Opens_Panel_With_The_Buttons_Of_Its_State(string tag, UpdateState state, string buttons)
    {
        var window = new MainWindow { Width = 1200, Height = 780 };
        var vm = (MainViewModel)window.DataContext!;
        vm.Update.Percent = 42;
        vm.Update.State = state;
        window.Show();
        Pump();

        var layer = window.FindControl<Panel>("UpdateLayer")!;
        Assert.False(layer.IsVisible);
        Bar(window).FindControl<Button>("Rozet")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.True(layer.IsVisible);

        var panel = window.FindControl<DustyBytes.App.Kabuk.GuncellemePaneli>("UpdatePanel")!;
        var expected = buttons.Split(',');
        foreach (var name in new[] { "IndirDugmesi", "IndirVeYukleDugmesi", "IptalDugmesi", "YukleDugmesi" })
            Assert.Equal(expected.Contains(name), panel.FindControl<Button>(name)!.IsVisible);

        window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(Output(), "panel-" + tag + ".png"));
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump();
        Assert.False(layer.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Badge_Hidden_Until_An_Update_Is_Found()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        Assert.False(Bar(window).FindControl<Button>("Rozet")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("sari", UpdateState.Available, 0, "Güncelleme", "Warning")]
    [InlineData("iniyor", UpdateState.Downloading, 42, "İniyor %42", "Warning")]
    [InlineData("yesil", UpdateState.Ready, 100, "Güncelleme", "Success")]
    public void Badge_States_Render_Right_Of_Title_And_Left_Of_Signature(string tag, UpdateState state, int percent, string text, string brush)
    {
        var window = new MainWindow { Width = 1200, Height = 780 };
        var vm = (MainViewModel)window.DataContext!;
        vm.Update.Percent = percent;
        vm.Update.State = state;
        window.Show();
        Pump();

        var bar = Bar(window);
        var badge = bar.FindControl<Button>("Rozet")!;
        var signature = bar.FindControl<Control>("DestekDugmesi")!;
        Assert.True(badge.IsEffectivelyVisible);
        Assert.Equal(text, bar.FindControl<TextBlock>("RozetYazisi")!.Text);
        var expected = (ISolidColorBrush)window.FindResource(brush)!;
        var dot = (ISolidColorBrush)badge.GetVisualDescendants().OfType<Ellipse>().First().Fill!;
        Assert.Equal(expected.Color, dot.Color);
        Assert.True(badge.Bounds.Height >= 24 && badge.Bounds.Width >= 24);
        var badgeRight = badge.TranslatePoint(new Point(badge.Bounds.Width, 0), window)!.Value.X;
        var sigLeft = signature.TranslatePoint(new Point(0, 0), window)!.Value.X;
        Assert.True(badgeRight <= sigLeft, $"rozet {badgeRight}, imza {sigLeft}");
        Assert.True(badgeRight > window.Bounds.Width / 2);

        window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(Output(), "rozet-" + tag + ".png"));
        window.Close();
    }
}
