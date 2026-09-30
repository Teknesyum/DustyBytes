using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Tests;

public class HatirlatmaTests
{
    const long GB = 1L << 30;

    sealed class FakeNotifier(bool ok = true) : INotifier
    {
        public List<(string Title, string Body, string Launch)> Shown { get; } = [];

        public bool Show(string title, string body, string launch)
        {
            Shown.Add((title, body, launch));
            return ok;
        }
    }

    sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<(string Key, string Name), string> Values { get; } = [];
        public int Writes { get; private set; }
        public List<string> Deleted { get; } = [];

        public string? Read(string key, string? name) => Values.GetValueOrDefault((key, name ?? ""));

        public void Write(string key, string? name, string value)
        {
            Writes++;
            Values[(key, name ?? "")] = value;
        }

        public void DeleteTree(string key)
        {
            Deleted.Add(key);
            foreach (var k in Values.Keys.Where(k => k.Key == key || k.Key.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
                Values.Remove(k);
        }

        public bool Exists(string key) => Values.Keys.Any(k => k.Key == key || k.Key.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase));
    }

    sealed class FakeScheduler : ICommandRunner
    {
        public string? Stored { get; set; }
        public List<string[]> Calls { get; } = [];
        public string? CreatedXml { get; private set; }

        public CommandResult Run(string file, IReadOnlyList<string> args)
        {
            Assert.Equal(WeeklyCheckTask.Tool, file);
            Calls.Add([.. args]);
            switch (args[0])
            {
                case "/Query":
                    return Stored is null ? new(1, "") : new(0, args.Contains("/XML") ? Stored : "ok");
                case "/Create":
                    var path = args[args.ToList().IndexOf("/XML") + 1];
                    CreatedXml = File.ReadAllText(path);
                    Stored = CreatedXml;
                    return new(0, "");
                case "/Delete":
                    Stored = null;
                    return new(0, "");
                default:
                    return new(1, "");
            }
        }
    }

    [Theory]
    [InlineData(new string[0], LaunchMode.Normal, null)]
    [InlineData(new[] { "--worker", "--pipe", "x" }, LaunchMode.Worker, null)]
    [InlineData(new[] { "--check" }, LaunchMode.Check, null)]
    [InlineData(new[] { "--CHECK" }, LaunchMode.Check, null)]
    [InlineData(new[] { "--unregister" }, LaunchMode.Unregister, null)]
    [InlineData(new[] { "--inspect", @"C:\Oyunlar\Eski Oyun" }, LaunchMode.Inspect, @"C:\Oyunlar\Eski Oyun")]
    [InlineData(new[] { "--inspect", "\"C:\\Oyunlar\\\"" }, LaunchMode.Inspect, @"C:\Oyunlar")]
    [InlineData(new[] { "--inspect", "D:" }, LaunchMode.Inspect, @"D:\")]
    [InlineData(new[] { "--inspect", "D:\"" }, LaunchMode.Inspect, @"D:\")]
    [InlineData(new[] { "--inspect" }, LaunchMode.Normal, null)]
    [InlineData(new[] { "--inspect", "" }, LaunchMode.Normal, null)]
    [InlineData(new[] { "--inspect", @"Oyunlar\Eski" }, LaunchMode.Normal, null)]
    [InlineData(new[] { "dustybytes:open" }, LaunchMode.Normal, null)]
    [InlineData(new[] { "--Worker" }, LaunchMode.Normal, null)]
    public void LaunchArgs_Parse_Modes(string[] args, LaunchMode mode, string? path)
    {
        var parsed = LaunchArgs.Parse(args);
        Assert.Equal(mode, parsed.Mode);
        Assert.Equal(path, parsed.Path);
    }

    static DriveSpace Drive(string root, double totalGb, double freeGb) => new(root, (long)(totalGb * GB), (long)(freeGb * GB));

    [Theory]
    [InlineData(500, 100, false, false)]
    [InlineData(500, 40, true, true)]
    [InlineData(500, 60, false, false)]
    [InlineData(200, 25, false, false)]
    [InlineData(200, 14, true, true)]
    [InlineData(100, 12, true, false)]
    [InlineData(1000, 90, true, true)]
    [InlineData(1000, 120, false, false)]
    [InlineData(32, 7, true, false)]
    [InlineData(32, 9, false, false)]
    [InlineData(0, 0, false, false)]
    public void DiskCheck_Thresholds(double totalGb, double freeGb, bool low, bool critical)
    {
        var d = Drive(@"C:\", totalGb, freeGb);
        Assert.Equal(low, DiskCheck.IsLow(d));
        Assert.Equal(critical && low, DiskCheck.IsCritical(d));
    }

    [Fact]
    public void DiskCheck_Title_Lists_Low_Drives_Fullest_First()
    {
        var low = DiskCheck.Low([Drive(@"C:\", 500, 40), Drive(@"D:\", 1000, 500), Drive(@"E:\", 100, 2)]);
        Assert.Equal([@"E:\", @"C:\"], low.Select(d => d.Root));
        Assert.Equal("E: %98 dolu, C: %92 dolu", DiskCheck.Title(low));
        Assert.Equal("E: %98 dolu, C: %92 dolu · DustyBytes ile yer açın", DiskCheck.Message(low));
    }

    [Fact]
    public void DiskCheck_Run_Notifies_Only_When_Low()
    {
        var quiet = new FakeNotifier();
        Assert.Equal(0, DiskCheck.Run([Drive(@"C:\", 500, 200)], quiet));
        Assert.Empty(quiet.Shown);

        var loud = new FakeNotifier();
        Assert.Equal(0, DiskCheck.Run([Drive(@"C:\", 500, 40)], loud));
        var shown = Assert.Single(loud.Shown);
        Assert.Equal("C: %92 dolu", shown.Title);
        Assert.Equal(DiskCheck.Body, shown.Body);
        Assert.Equal(LaunchArgs.OpenUri, shown.Launch);

        Assert.Equal(1, DiskCheck.Run([Drive(@"C:\", 500, 40)], new FakeNotifier(ok: false)));
    }

    static QuarantineEntry Held(string id, string original, long size, string? root = null) => new()
    {
        Id = id,
        OriginalPath = original,
        Root = root ?? Path.GetPathRoot(original) + "$DustyBytes",
        Size = size,
        MovedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddDays(30),
    };

    [Fact]
    public void DiskCheck_FreeNow_Picks_Critical_Drive_With_Pending_Items()
    {
        var entries = new[]
        {
            Held("a", @"C:\Oyunlar\Eski", 4 * GB),
            Held("b", @"C:\Videolar\film.mkv", 2 * GB),
            Held("c", @"D:\Arşiv", 9 * GB),
            Held("z", @"E:\Boş", 0),
        };
        Assert.Null(DiskCheck.FreeNow([Drive(@"C:\", 500, 100), Drive(@"D:\", 1000, 400)], entries));

        var offer = DiskCheck.FreeNow([Drive(@"C:\", 500, 40), Drive(@"D:\", 1000, 20), Drive(@"E:\", 100, 1)], entries);
        Assert.NotNull(offer);
        Assert.Equal(@"D:\", offer.Drive.Root);
        Assert.Equal(9 * GB, offer.Bytes);
        Assert.Equal(["c"], offer.Ids);

        var cOnly = DiskCheck.FreeNow([Drive(@"c:\", 500, 40)], entries);
        Assert.NotNull(cOnly);
        Assert.Equal(6 * GB, cOnly.Bytes);
        Assert.Equal(["a", "b"], cOnly.Ids);

        Assert.Null(DiskCheck.FreeNow([Drive(@"E:\", 100, 1)], entries));
    }

    [Fact]
    public void Ledger_Keeps_History_And_Sums_The_Month()
    {
        var dir = Directory.CreateTempSubdirectory("dustybytes-ledger-");
        try
        {
            var ledger = new Ledger(Path.Combine(dir.FullName, "ledger.json"));
            var now = DateTimeOffset.Now;
            var lastMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddDays(-2);
            ledger.Add(5 * GB, Drive(@"C:\", 500, 100), lastMonth);
            ledger.Add(2 * GB, Drive(@"C:\", 500, 102), now);
            var data = ledger.Add(3 * GB, Drive(@"D:\", 1000, 403), now);

            Assert.Equal(10 * GB, data.FreedBytes);
            Assert.Equal(3, data.Actions);
            Assert.Equal(5 * GB, data.FreedInMonth(now));

            var read = new Ledger(ledger.Path).Read();
            Assert.Equal(3, read.Entries.Count);
            var last = read.Last!;
            Assert.Equal(@"D:\", last.Root);
            Assert.True(last.HasDisk);
            Assert.Equal(0.6, last.UsedBefore, 3);
            Assert.Equal(0.597, last.UsedAfter, 3);
            Assert.False(File.Exists(ledger.Path + ".tmp"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Ledger_Reads_Old_File_And_Caps_History()
    {
        var dir = Directory.CreateTempSubdirectory("dustybytes-ledger-");
        try
        {
            var path = Path.Combine(dir.FullName, "ledger.json");
            File.WriteAllText(path, """{ "freedBytes": 7, "actions": 2 }""");
            var ledger = new Ledger(path);
            var old = ledger.Read();
            Assert.Equal(7, old.FreedBytes);
            Assert.Empty(old.Entries);
            Assert.Null(old.Last);

            var start = DateTimeOffset.Now.AddDays(-1);
            for (var i = 0; i < Ledger.HistoryMax + 5; i++)
                ledger.Add(1, null, start.AddSeconds(i));
            var data = ledger.Read();
            Assert.Equal(Ledger.HistoryMax, data.Entries.Count);
            Assert.Equal(7 + Ledger.HistoryMax + 5, data.FreedBytes);
            Assert.Equal(start.AddSeconds(Ledger.HistoryMax + 4), data.Last!.At);
            Assert.False(data.Last.HasDisk);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void ShellIntegration_Writes_Once_And_Removes_All_Keys()
    {
        var registry = new FakeRegistry();
        var shell = new ShellIntegration(registry, @"C:\Araçlar\DustyBytes\DustyBytes.exe");
        var first = shell.Ensure();
        Assert.True(first >= 8);
        Assert.Equal("DustyBytes ile incele", registry.Read(ShellIntegration.MenuKey, null));
        Assert.Equal("\"C:\\Araçlar\\DustyBytes\\DustyBytes.exe\" --inspect \"%1\"", registry.Read(ShellIntegration.MenuKey + @"\command", null));
        Assert.Equal("", registry.Read(ShellIntegration.ProtocolKey, "URL Protocol"));
        Assert.Equal("DustyBytes", registry.Read(ShellIntegration.AppIdKey, "DisplayName"));

        Assert.Equal(0, shell.Ensure());
        Assert.Equal(first, registry.Writes);

        registry.Write(ShellIntegration.MenuKey + @"\command", null, "eski.exe --inspect \"%1\"");
        Assert.Equal(1, shell.Ensure());

        Assert.Equal(3, shell.Remove());
        Assert.Empty(registry.Values);
        Assert.Equal(0, shell.Remove());
    }

    [Fact]
    public void WeeklyCheckTask_Creates_Once_And_Deletes_When_Off()
    {
        var dir = Directory.CreateTempSubdirectory("dustybytes-task-");
        try
        {
            var runner = new FakeScheduler();
            var task = new WeeklyCheckTask(runner, @"C:\Araçlar\Dusty & Bytes\DustyBytes.exe", dir.FullName);

            Assert.True(task.Ensure(true));
            Assert.Contains(runner.Calls, c => c[0] == "/Create" && c.Contains("/F") && c.Contains(WeeklyCheckTask.TaskName));
            Assert.Contains("<Arguments>--check</Arguments>", runner.CreatedXml);
            Assert.Contains(@"C:\Araçlar\Dusty &amp; Bytes\DustyBytes.exe", runner.CreatedXml);
            Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", runner.CreatedXml);
            Assert.Empty(dir.GetFiles());

            runner.Calls.Clear();
            Assert.True(task.Ensure(true));
            Assert.DoesNotContain(runner.Calls, c => c[0] == "/Create");

            var moved = new WeeklyCheckTask(runner, @"D:\Yeni\DustyBytes.exe", dir.FullName);
            runner.Calls.Clear();
            Assert.True(moved.Ensure(true));
            Assert.Contains(runner.Calls, c => c[0] == "/Create");

            runner.Calls.Clear();
            Assert.True(moved.Ensure(false));
            Assert.Contains(runner.Calls, c => c[0] == "/Delete" && c.Contains(WeeklyCheckTask.TaskName));
            Assert.Null(runner.Stored);

            runner.Calls.Clear();
            Assert.True(moved.Ensure(false));
            Assert.DoesNotContain(runner.Calls, c => c[0] == "/Delete");
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void SystemIntegration_Unregister_Removes_Task_And_Keys()
    {
        var dir = Directory.CreateTempSubdirectory("dustybytes-task-");
        try
        {
            var registry = new FakeRegistry();
            var runner = new FakeScheduler();
            var exe = @"C:\Araçlar\DustyBytes.exe";
            var integration = new SystemIntegration(new ShellIntegration(registry, exe), new WeeklyCheckTask(runner, exe, dir.FullName));
            var applied = integration.Apply(true);
            Assert.True(applied.ShellOk && applied.TaskOk);
            Assert.Null(applied.Error);
            Assert.NotNull(runner.Stored);

            var off = integration.Apply(false);
            Assert.True(off.TaskOk);
            Assert.Null(runner.Stored);
            Assert.NotEmpty(registry.Values);

            integration.Apply(true);
            var gone = integration.Unregister();
            Assert.True(gone.ShellOk && gone.TaskOk);
            Assert.Null(runner.Stored);
            Assert.Empty(registry.Values);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Map_Nearest_Falls_Back_To_Parent_Folder()
    {
        var root = FakeBackend.Snapshot().Result.Root;
        var (exact, isExact) = MapViewModel.Nearest(root, @"C:\Oyunlar\Eski Oyun");
        Assert.True(isExact);
        Assert.Equal("Eski Oyun", exact!.Name);

        var (parent, parentExact) = MapViewModel.Nearest(root, @"C:\Oyunlar\Silinmiş\Alt");
        Assert.False(parentExact);
        Assert.Equal("Oyunlar", parent!.Name);

        var (none, _) = MapViewModel.Nearest(root, @"Q:\Yok");
        Assert.Null(none);
    }

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Inspect_Opens_Map_On_That_Folder()
    {
        var vm = new MainViewModel(new FakeBackend());
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.Inspect(@"C:\Videolar\Film");
        Assert.Same(vm.Map, vm.Navigation.Current);
        Assert.Equal("Film", vm.Map.Current!.Name);
        Assert.Equal(@"C:\Videolar\Film", vm.Map.FocusPath);
    }

    [AvaloniaFact]
    public async Task Overview_FreeNow_Needs_Two_Presses_And_Purges_Pending_Items()
    {
        var backend = FakeBackend.Rich();
        backend.DriveList = [new DriveSpace(@"C:\", 100 * GB, 5 * GB)];
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, FreedBytes = 6_000_000_000 };
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        await vm.Session.RefreshQuarantineAsync(vm);
        await Settle();

        var overview = vm.Overview;
        Assert.True(overview.HasFreeNow);
        Assert.Equal("C: sürücüsünde yer azaldı", overview.FreeNowTitle);
        Assert.StartsWith("Bekleyen ", overview.FreeNowButtonText);
        Assert.EndsWith("'ı şimdi kalıcı sil", overview.FreeNowButtonText);

        await overview.FreeNowCommand.ExecuteAsync(null);
        Assert.True(overview.IsFreeNowArmed);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Purge);

        await overview.FreeNowCommand.ExecuteAsync(null);
        await Settle();
        var purge = Assert.Single(backend.Requests, r => r.Op == Ops.Purge);
        Assert.True(purge.UserApproved);
        Assert.Equal(["q1", "q2"], purge.Items);
        Assert.Equal([@"C:\"], backend.FreedRoots);
        Assert.StartsWith("Bu ay ", overview.MonthText);
        Assert.True(overview.HasLastCleanup);
        Assert.Equal(2, overview.Compare.Count);
    }

    [AvaloniaFact]
    public async Task Overview_FreeNow_Hidden_When_Space_Is_Fine()
    {
        var backend = FakeBackend.Rich();
        var vm = new MainViewModel(backend);
        await vm.Session.RefreshQuarantineAsync(vm);
        await Settle();
        Assert.False(vm.Overview.HasFreeNow);
        Assert.False(vm.Overview.FreeNowCommand.CanExecute(null));
        Assert.Equal("Bu ay henüz yer açılmadı", vm.Overview.MonthText);
        Assert.False(vm.Overview.HasLastCleanup);
    }

    [AvaloniaFact]
    public async Task Overview_WeeklyCheck_Toggle_Goes_Through_Backend()
    {
        var backend = new FakeBackend { WeeklyCheck = true };
        var vm = new MainViewModel(backend);
        Assert.True(vm.Overview.WeeklyCheck);
        Assert.Empty(backend.WeeklyCalls);
        vm.Overview.WeeklyCheck = false;
        await Settle();
        Assert.Equal([false], backend.WeeklyCalls);
        Assert.False(backend.WeeklyCheck);
    }
}
