using System.Runtime.CompilerServices;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Tests;

public static class TestSetup
{
    [ModuleInitializer]
    public static void Init()
    {
        Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", Environment.GetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION") ?? "0");
        MainWindow.ContextFactory = () =>
        {
            var vm = new MainViewModel(FakeBackend.Rich(), Environment.GetEnvironmentVariable("DUSTYBYTES_START_SCREEN"));
            vm.Session.SetSnapshot(FakeBackend.Snapshot());
            return vm;
        };
    }
}

public sealed class FakeBackend : IAppBackend
{
    public List<WorkerRequest> Requests { get; } = [];
    public Func<WorkerRequest, WorkerResponse>? Respond { get; set; }
    public ScanSnapshot? Cached { get; set; }
    public ScanSnapshot? Fresh { get; set; }
    public Exception? ScanError { get; set; }
    public List<ProgramInfo> Programs { get; set; } = [];
    public LeftoverSnapshot? Preview { get; set; }
    public List<CleanRuleInfo> Rules { get; set; } = [];
    public List<SystemTaskInfo> Tasks { get; set; } = [];
    public QuarantineSnapshot Quarantine { get; set; } = new([], [], true, null);
    public LedgerData Ledger { get; private set; } = new(0, 0);
    public Availability Fast { get; set; } = new(false, "Yönetici süreci çalışmıyor");

    public string ScanRoot => @"C:\";
    public bool DryRun { get; set; }
    public bool WorkerRunning { get; set; }
    public bool Winapp2Present { get; set; }

    public static FakeBackend Rich()
    {
        var now = DateTime.UtcNow;
        return new FakeBackend
        {
            Cached = Snapshot(),
            Fresh = Snapshot(),
            Programs =
            [
                new ProgramInfo(Program("p1", "Eski Editör", "Örnek Yazılım", 900_000_000), new UsageSignal(DateTimeOffset.Now.AddDays(-400), "prefetch", 0.9)),
                new ProgramInfo(Program("p2", "Oyun Başlatıcı", "Başka Firma", 300_000_000), UsageSignal.Unknown),
            ],
            Preview = Leftovers(false),
            Rules =
            [
                new CleanRuleInfo(new CleanerRule
                {
                    Id = "chrome",
                    Name = "Chrome",
                    Options =
                    [
                        new CleanerOption { Id = "cache", Label = "Önbellek" },
                        new CleanerOption { Id = "cookies", Label = "Çerezler", Warning = "Oturumlar kapanır" },
                    ],
                }, false, null),
                new CleanRuleInfo(new CleanerRule
                {
                    Id = "edge",
                    Name = "Edge",
                    Source = "Winapp2",
                    Options = [new CleanerOption { Id = "cache", Label = "Önbellek" }],
                }, true, "msedge"),
            ],
            Tasks =
            [
                new SystemTaskInfo("temp", "Windows geçici dosyaları", 120_000_000, true, "Sistem geçici klasörü", true, null),
                new SystemTaskInfo("component-store", "Bileşen deposu", 0, false, "DISM", false, "Bu sürümde kapalı"),
            ],
            Quarantine = new QuarantineSnapshot(
            [
                Entry("q1", @"C:\Oyunlar\Eski", now.AddDays(-3), 4_000_000_000),
                Entry("q2", @"C:\Kullanıcılar\ben\Videolar\film.mkv", now.AddDays(-29), 2_000_000_000),
            ],
            [new VolumeUsage(@"C:\", "vol", 2, 6_000_000_000, 100_000_000_000, true)], true, null),
        };
    }

    public static InstalledProgram Program(string id, string name, string publisher, long size) => new()
    {
        Id = id,
        DisplayName = name,
        Publisher = publisher,
        DisplayVersion = "1.0",
        SizeBytes = size,
        UninstallString = "uninstall.exe",
    };

    public static QuarantineEntry Entry(string id, string path, DateTime moved, long size) => new()
    {
        Id = id,
        OriginalPath = path,
        Root = @"C:\$DustyBytes",
        Size = size,
        MovedUtc = moved,
        ExpiresUtc = moved.AddDays(30),
    };

    public static LeftoverSnapshot Leftovers(bool diff, bool stillInstalled = false) => new()
    {
        Id = "snap1",
        Program = Program("p1", "Eski Editör", "Örnek Yazılım", 900_000_000),
        IsDiff = diff,
        ProgramStillInstalled = stillInstalled,
        Candidates =
        [
            new LeftoverCandidate { Id = "c1", Kind = LeftoverKind.Folder, Target = @"C:\Program Files\Eski Editör", Tier = ConfidenceTier.High, Reason = "Kurulum klasörü", Bytes = 50_000_000 },
            new LeftoverCandidate { Id = "c2", Kind = LeftoverKind.Folder, Target = @"C:\ProgramData\Eski", Tier = ConfidenceTier.Medium, Reason = "Ad eşleşmesi", Bytes = 1_000_000 },
            new LeftoverCandidate { Id = "c3", Kind = LeftoverKind.Folder, Target = @"C:\Temp\eski", Tier = ConfidenceTier.Low, Reason = "Zayıf eşleşme" },
        ],
    };

    public static ScanSnapshot Snapshot()
    {
        var root = new ScanNode { Name = @"C:\", IsDirectory = true, Children = [] };
        ScanNode Dir(ScanNode parent, string name, long size)
        {
            var node = new ScanNode { Name = name, IsDirectory = true, Parent = parent, Size = size, Children = [] };
            parent.Children!.Add(node);
            parent.Size += size;
            return node;
        }
        var games = Dir(root, "Oyunlar", 0);
        Dir(games, "Eski Oyun", 40_000_000_000);
        Dir(games, "Yeni Oyun", 20_000_000_000);
        var videos = Dir(root, "Videolar", 0);
        Dir(videos, "Film", 8_000_000_000);
        var dev = Dir(root, "Kod", 0);
        Dir(dev, "node_modules", 3_000_000_000);
        Dir(root, "Windows", 25_000_000_000);
        for (var i = 0; i < 30; i++)
            Dir(root, "Küçük" + i, 1_000_000);

        var old = DateTimeOffset.Now.AddDays(-500);
        IReadOnlyList<Unit> units =
        [
            new Unit { Id = "u1", Kind = UnitKind.Game, Name = "Eski Oyun", Paths = [@"C:\Oyunlar\Eski Oyun"], SizeBytes = 40_000_000_000, Usage = new UsageSignal(old, "prefetch", 0.9), Score = 9, Reason = "Bir yıldır açılmadı" },
            new Unit { Id = "u2", Kind = UnitKind.Game, Name = "Yeni Oyun", Paths = [@"C:\Oyunlar\Yeni Oyun"], SizeBytes = 20_000_000_000, Removal = RemovalMethod.Launcher, LauncherUri = "steam://uninstall/1", Score = 5 },
            new Unit { Id = "u3", Kind = UnitKind.Film, Name = "Film", Paths = [@"C:\Videolar\Film"], SizeBytes = 8_000_000_000, ContainsUserData = true, Score = 4 },
            new Unit { Id = "u4", Kind = UnitKind.DevArtifact, Name = "node_modules", Paths = [@"C:\Kod\node_modules"], SizeBytes = 3_000_000_000, Removal = RemovalMethod.DirectDelete, Score = 3 },
            new Unit { Id = "u5", Kind = UnitKind.Program, Name = "Eski Editör", Paths = [@"C:\Program Files\Eski Editör"], SizeBytes = 900_000_000, Removal = RemovalMethod.Uninstaller, Score = 2 },
        ];
        var result = new ScanResult { Root = root, Files = 1000, Directories = 40 };
        return new ScanSnapshot(result, units, DateTimeOffset.Now.AddMinutes(-5), "FindFirstFileEx");
    }

    public Task<ScanSnapshot?> LoadCachedAsync(CancellationToken ct) => Task.FromResult(Cached);

    public async Task<ScanSnapshot> ScanAsync(IProgress<TaskStep> progress, CancellationToken ct)
    {
        await Task.Yield();
        if (ScanError is not null)
            throw ScanError;
        progress.Report(new TaskStep("Taranıyor", 50, "C:\\Oyunlar"));
        return Fresh ?? Snapshot();
    }

    public Availability FastScanAvailability() => Fast;

    public Task<ScanSnapshot> FastScanAsync(IProgress<TaskStep> progress, CancellationToken ct) => ScanAsync(progress, ct);

    public Task<IReadOnlyList<ProgramInfo>> ListProgramsAsync(IProgress<TaskStep> progress, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProgramInfo>>(Programs);

    public Task<LeftoverSnapshot> PreviewLeftoversAsync(InstalledProgram program, IProgress<TaskStep> progress, CancellationToken ct) =>
        Task.FromResult(Preview ?? Leftovers(false));

    public Task<IReadOnlyList<CleanRuleInfo>> CleanRulesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CleanRuleInfo>>(Rules);

    public Task<IReadOnlyList<OptionPreview>> PreviewCleanAsync(IReadOnlyList<RuleSelection> selection, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OptionPreview>>([.. selection.SelectMany(s => s.OptionIds.Select(o => new OptionPreview(s.RuleId, o, 3, 1_000_000)))]);

    public Task<IReadOnlyList<SystemTaskInfo>> SystemTasksAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SystemTaskInfo>>(Tasks);

    public Task<QuarantineSnapshot> ReadQuarantineAsync(CancellationToken ct) => Task.FromResult(Quarantine);

    public Task<WorkerResponse> SendAsync(WorkerRequest request, IProgress<TaskStep> progress, CancellationToken ct)
    {
        Requests.Add(request);
        var response = Respond?.Invoke(request) ?? new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            DryRun = DryRun,
            Items = [.. request.Paths.Select((p, i) => new ItemResult($"{p}|{request.UnitId}-{i}", true, "", 0))],
        };
        return Task.FromResult(response);
    }

    public LedgerData ReadLedger() => Ledger;

    public LedgerData AddFreed(long bytes) => Ledger = new(Ledger.FreedBytes + bytes, Ledger.Actions + 1);
}
