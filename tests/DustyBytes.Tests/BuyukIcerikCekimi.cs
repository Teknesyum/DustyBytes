using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.ViewModels;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;
using DustyBytes.Units;

namespace DustyBytes.Tests;

public sealed class BuyukIcerikCekimi
{
    const string Variable = "DUSTYBYTES_CEKIM";
    const string Home = @"C:\Users\Administrator";

    static readonly (string Relative, long Size)[] Real =
    [
        (@".lmstudio\models\0bserverx\Qwen3.8-27B-Heretic-Abliterated-Uncensored-GGUF", 629_247_008),
        (@".lmstudio\models\Blackfrost-Research\Muse-Glimmer-30B-Abliterated-GGUF", 16_935_294_080),
        (@".android\avd\a8_test.avd", 4_336_808_249),
        (@".nuget\packages", 7_245_023_542),
        (@"AppData\Local\npm-cache", 2_759_181_697),
        (@"AppData\Local\pip\cache", 7_958_315_971),
        (@".cargo\registry", 706_888_626),
    ];

    sealed class NoUsage : IUsageIndex
    {
        public UsageSignal ForExecutable(string exePath) => UsageSignal.Unknown;
        public UsageSignal ForFolder(string folder) => UsageSignal.Unknown;
        public UsageSignal ForMedia(string filePath) => UsageSignal.Unknown;
        public IReadOnlyList<GameInstall> Games => [];
    }

    static ScanNode Tree()
    {
        var root = new ScanNode { Name = @"C:\", IsDirectory = true, Children = [] };
        foreach (var (relative, size) in Real)
        {
            var full = Path.Combine(Home, relative);
            var written = Directory.Exists(full) ? Directory.GetLastWriteTimeUtc(full) : DateTime.UtcNow.AddDays(-120);
            var node = root;
            foreach (var part in full[3..].Split('\\'))
            {
                var next = node.Children!.FirstOrDefault(c => c.Name == part);
                if (next is null)
                {
                    next = new ScanNode { Name = part, IsDirectory = true, Parent = node, Children = [] };
                    node.Children!.Add(next);
                }
                next.Size += size;
                next.NewestWriteTicks = Math.Max(next.NewestWriteTicks, written.Ticks);
                node = next;
            }
            root.Size += size;
        }
        return root;
    }

    static void Pump()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        Pump();
    }

    [AvaloniaFact]
    public async Task Buyuk_Icerik_Ekranlari()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } output)
            return;
        Directory.CreateDirectory(output);

        var root = Tree();
        var ctx = new UnitContext
        {
            ScanResult = new ScanResult { Root = root, Files = 1000, Directories = 40 },
            UsageIndex = new NoUsage(),
            Protected = ProtectedList.LoadDefault(),
            Now = DateTimeOffset.Now,
        };
        var units = UnitBuilder.Build(ctx);
        File.WriteAllLines(Path.Combine(output, "birimler.txt"),
            units.Select(u => $"{Format.Bytes(u.SizeBytes)}\t{u.Label ?? u.Kind.ToString()}\t{u.Name}\t{u.Effect}"));

        var now = DateTime.UtcNow;
        var muse = units.First(u => u.Name.StartsWith("Muse", StringComparison.Ordinal));
        var backend = new FakeBackend
        {
            Cached = new ScanSnapshot(ctx.ScanResult, units, DateTimeOffset.Now.AddMinutes(-2), "FindFirstFileEx"),
            Quarantine = new QuarantineSnapshot(
            [
                FakeBackend.Entry("q1", muse.Paths[0], now.AddMinutes(-1), muse.SizeBytes),
                FakeBackend.Entry("q2", Path.Combine(Home, @"AppData\Local\npm-cache"), now.AddDays(-8), 2_759_181_697),
            ],
            [new VolumeUsage(@"C:\", "vol", 2, 19_700_000_000, 100_000_000_000, true)], true, null),
        };
        backend.Fresh = backend.Cached;

        var saved = MainWindow.ContextFactory;
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(backend.Cached);
        MainWindow.ContextFactory = () => vm;
        try
        {
            var window = new MainWindow { Width = 1200, Height = 780 };
            window.Show();
            await Settle();

            vm.GoTo(vm.Offers);
            await vm.Offers.Ready;
            await Settle();
            window.CaptureRenderedFrame()?.Save(Path.Combine(output, "oneriler.png"));
            File.WriteAllLines(Path.Combine(output, "oneriler-kartlar.txt"),
                vm.Offers.Cards.Select(c => $"{Format.Bytes(c.Unit.SizeBytes)}\t{c.KindLabel}\t{c.Unit.Name}\t{c.ActionText}\t{c.Effect}"));

            await vm.Offers.RemoveOneCommand.ExecuteAsync(vm.Offers.Cards.First(c => c.Unit.Id == muse.Id));
            await Settle();
            window.CaptureRenderedFrame()?.Save(Path.Combine(output, "tek-tik-bildirim.png"));
            File.WriteAllLines(Path.Combine(output, "tek-tik-istek.txt"),
                backend.Requests.Select(r => $"{r.Op}\t{r.UnitId}\tIncludeUserData={r.IncludeUserData}\tUserApproved={r.UserApproved}")
                    .Append($"Onay penceresi: {(vm.Confirm is null ? "yok" : "VAR")}")
                    .Concat(vm.Toasts.Select(t => "Bildirim: " + t.Message)));

            vm.GoTo(vm.Quarantine);
            await Settle();
            await Task.Delay(200);
            await Settle();
            window.CaptureRenderedFrame()?.Save(Path.Combine(output, "karantina.png"));
            window.Close();
        }
        finally
        {
            MainWindow.ContextFactory = saved;
        }
    }
}
