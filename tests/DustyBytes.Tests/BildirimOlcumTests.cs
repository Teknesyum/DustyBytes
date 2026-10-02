using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class BildirimOlcumTests
{
    const long GB = 1L << 30;
    const string Token = "0123456789abcdef0123456789abcdef";

    sealed class ActionNotifier(bool ok = true) : IActionNotifier
    {
        public List<(string Title, string Body, string Launch, IReadOnlyList<ToastAction> Actions)> Shown { get; } = [];

        public bool Show(string title, string body, string launch) => Show(title, body, launch, []);

        public bool Show(string title, string body, string launch, IReadOnlyList<ToastAction> actions)
        {
            Shown.Add((title, body, launch, actions));
            return ok;
        }
    }

    sealed class PlainNotifier : INotifier
    {
        public int Count { get; private set; }

        public bool Show(string title, string body, string launch)
        {
            Count++;
            return true;
        }
    }

    sealed class Temp : IDisposable
    {
        public DirectoryInfo Dir { get; } = Directory.CreateTempSubdirectory("dustybytes-bildirim-");
        public string File(string name) => Path.Combine(Dir.FullName, name);
        public void Dispose() => Dir.Delete(true);
    }

    static DriveSpace Drive(double totalGb, double freeGb) => new(@"C:\", (long)(totalGb * GB), (long)(freeGb * GB));

    static readonly DateTimeOffset Monday = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("--safe-clean", LaunchMode.SafeClean, null)]
    [InlineData("--SAFE-CLEAN", LaunchMode.SafeClean, null)]
    [InlineData("dustybytes:safe-clean?t=" + Token, LaunchMode.SafeClean, Token)]
    [InlineData("dustybytes:safe-clean?t=" + Token + "/", LaunchMode.SafeClean, Token)]
    [InlineData("\"dustybytes:SAFE-CLEAN?t=" + Token + "\"", LaunchMode.SafeClean, Token)]
    [InlineData("dustybytes:snooze?t=" + Token, LaunchMode.Snooze, Token)]
    [InlineData("dustybytes:mute?t=" + Token, LaunchMode.Mute, Token)]
    [InlineData("dustybytes:safe-clean", LaunchMode.Normal, null)]
    [InlineData("dustybytes:safe-clean?t=", LaunchMode.Normal, null)]
    [InlineData("dustybytes:safe-clean?t=XYZ", LaunchMode.Normal, null)]
    [InlineData("dustybytes:mute", LaunchMode.Normal, null)]
    [InlineData("dustybytes:open", LaunchMode.Normal, null)]
    [InlineData("dustybytes:bilinmeyen?t=" + Token, LaunchMode.Normal, null)]
    public void LaunchArgs_Parse_Notification_Actions(string arg, LaunchMode mode, string? token)
    {
        var parsed = LaunchArgs.Parse([arg]);
        Assert.Equal(mode, parsed.Mode);
        Assert.Equal(token, parsed.Token);
    }

    [Fact]
    public void ActionUri_Round_Trips_Through_Parse()
    {
        foreach (var (action, mode) in new[] { (LaunchArgs.SafeCleanAction, LaunchMode.SafeClean), (LaunchArgs.SnoozeAction, LaunchMode.Snooze), (LaunchArgs.MuteAction, LaunchMode.Mute) })
        {
            var parsed = LaunchArgs.Parse([LaunchArgs.ActionUri(action, Token)]);
            Assert.Equal(mode, parsed.Mode);
            Assert.Equal(Token, parsed.Token);
        }
    }

    [Fact]
    public void Budget_Needs_Reclaimable_Or_Critical_Disk()
    {
        var critical = new[] { Drive(500, 40) };
        var onlyBytes = new[] { Drive(500, 70) };
        var fine = new[] { Drive(500, 300) };
        var none = new NoticeData();
        var big = new NoticeData(ReclaimableBytes: 5 * GB);
        var small = new NoticeData(ReclaimableBytes: 5 * GB - 1);

        Assert.True(NoticePolicy.Allows(critical, none, Monday));
        Assert.False(NoticePolicy.Allows(onlyBytes, none, Monday));
        Assert.False(NoticePolicy.Allows(onlyBytes, small, Monday));
        Assert.True(NoticePolicy.Allows(onlyBytes, big, Monday));
        Assert.False(NoticePolicy.Allows(DiskCheck.Low(fine), big, Monday));
    }

    [Fact]
    public void Budget_Allows_One_Notice_Per_Week()
    {
        var critical = new[] { Drive(500, 40) };
        var shownAt = new NoticeData(LastShown: Monday);
        Assert.False(NoticePolicy.Allows(critical, shownAt, Monday.AddDays(3)));
        Assert.False(NoticePolicy.Allows(critical, shownAt, Monday.AddDays(5)));
        Assert.True(NoticePolicy.Allows(critical, shownAt, Monday.AddDays(7).AddSeconds(-30)));
        Assert.True(NoticePolicy.Allows(critical, shownAt, Monday.AddDays(7)));
    }

    [Fact]
    public void Budget_Snooze_Silences_Until_It_Ends()
    {
        var critical = new[] { Drive(500, 40) };
        var data = new NoticeData(SnoozedUntil: Monday.AddDays(7));
        Assert.False(NoticePolicy.Allows(critical, data, Monday.AddDays(6)));
        Assert.True(NoticePolicy.Allows(critical, data, Monday.AddDays(7)));
    }

    [Fact]
    public void Run_Shows_At_Most_One_Notice_A_Week_With_Three_Actions()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        state.RecordEstimate(14 * GB, 30 * GB, Monday);
        var notifier = new ActionNotifier();
        var drives = new[] { Drive(500, 40) };

        Assert.Equal(0, DiskCheck.Run(drives, notifier, null, state, Monday));
        var shown = Assert.Single(notifier.Shown);
        Assert.Equal(LaunchArgs.OpenUri, shown.Launch);
        Assert.Equal(3, shown.Actions.Count);
        Assert.Equal($"Güvenli temizle (≈{Format.Bytes(14 * GB)})", shown.Actions[0].Content);
        Assert.Equal("Bu hafta sus", shown.Actions[1].Content);
        Assert.Equal("Bir daha gösterme", shown.Actions[2].Content);
        var modes = shown.Actions.Select(a => LaunchArgs.Parse([a.Uri])).ToList();
        Assert.Equal([LaunchMode.SafeClean, LaunchMode.Snooze, LaunchMode.Mute], modes.Select(m => m.Mode));
        Assert.All(modes, m => Assert.Equal(state.Read().Token, m.Token));
        Assert.Equal(Monday, state.Read().LastShown);

        Assert.Equal(0, DiskCheck.Run(drives, notifier, null, state, Monday.AddDays(2)));
        Assert.Single(notifier.Shown);
        Assert.Equal(0, DiskCheck.Run(drives, notifier, null, state, Monday.AddDays(7)));
        Assert.Equal(2, notifier.Shown.Count);
    }

    [Fact]
    public void Run_Does_Not_Count_A_Failed_Notice()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var notifier = new ActionNotifier(ok: false);
        Assert.Equal(1, DiskCheck.Run([Drive(500, 40)], notifier, null, state, Monday));
        Assert.Null(state.Read().LastShown);
    }

    [Fact]
    public void Run_Keeps_Quiet_For_Moderate_Disk_Without_Reclaimable_Space()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var notifier = new ActionNotifier();
        Assert.Equal(0, DiskCheck.Run([Drive(100, 12)], notifier, null, state, Monday));
        Assert.Empty(notifier.Shown);
        state.RecordEstimate(1 * GB, 8 * GB, Monday);
        Assert.Equal(0, DiskCheck.Run([Drive(100, 12)], notifier, null, state, Monday));
        Assert.Single(notifier.Shown);
    }

    [Fact]
    public void Run_Falls_Back_To_Plain_Toast_Without_Action_Support()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var plain = new PlainNotifier();
        Assert.Equal(0, DiskCheck.Run([Drive(500, 40)], plain, null, state, Monday));
        Assert.Equal(1, plain.Count);
        Assert.Null(state.Read().Token);
    }

    [Fact]
    public void Safe_Button_Says_Approximate_And_Hides_When_Nothing_Is_Safe()
    {
        Assert.Equal("Güvenli temizle", NoticePolicy.SafeButtonText(new NoticeData()));
        Assert.Null(NoticePolicy.SafeButtonText(new NoticeData(0, 9 * GB, Monday)));
        Assert.Equal($"Güvenli temizle (≈{Format.Bytes(3 * GB)})", NoticePolicy.SafeButtonText(new NoticeData(3 * GB, 9 * GB, Monday)));
        Assert.Equal(2, NoticePolicy.Actions(new NoticeData(0, 9 * GB, Monday), Token).Count);
    }

    [Fact]
    public void Snooze_Takes_A_Valid_Token_Once_And_Blocks_The_Next_Notice()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var token = state.IssueToken(Monday);

        Assert.False(state.Snooze("ffffffffffffffffffffffffffffffff", Monday));
        Assert.False(state.Snooze(null, Monday));
        Assert.True(state.Snooze(token, Monday.AddDays(1)));
        Assert.False(state.Snooze(token, Monday.AddDays(1)));
        Assert.Equal(Monday.AddDays(8), state.Read().SnoozedUntil);
        Assert.False(NoticePolicy.Allows([Drive(500, 40)], state.Read(), Monday.AddDays(7)));
        Assert.True(NoticePolicy.Allows([Drive(500, 40)], state.Read(), Monday.AddDays(8)));
    }

    [Fact]
    public void Token_Expires()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var token = state.IssueToken(Monday);
        Assert.False(state.Redeem(token, Monday + NoticeState.TokenLife + TimeSpan.FromMinutes(1)));
        token = state.IssueToken(Monday);
        Assert.True(state.Redeem(token, Monday + NoticeState.TokenLife));
    }

    [Fact]
    public void Mute_Disables_The_Weekly_Check_Only_With_A_Valid_Token()
    {
        using var temp = new Temp();
        var state = new NoticeState(temp.File("notice.json"));
        var disabled = 0;
        var token = state.IssueToken(Monday);

        Assert.False(NoticeActions.Mute(state, "bozuk", () => { disabled++; return true; }, Monday));
        Assert.Equal(0, disabled);
        Assert.True(NoticeActions.Mute(state, token, () => { disabled++; return true; }, Monday));
        Assert.Equal(1, disabled);
        Assert.False(NoticeActions.Mute(state, token, () => { disabled++; return true; }, Monday));
        Assert.Equal(1, disabled);
    }

    [Fact]
    public void Olcum_Writes_Lines_With_Time_And_Session_And_Never_Repeats_Once_Events()
    {
        using var temp = new Temp();
        var path = temp.File("olcum.jsonl");
        var now = Monday;
        var olcum = new Olcum(path, () => now, "oturum1");
        olcum.Write(OlcumKind.Start);
        now = now.AddSeconds(2);
        olcum.Write(OlcumKind.FirstCard, once: true);
        olcum.Write(OlcumKind.FirstCard, once: true);
        olcum.Write(OlcumKind.FirstFreed, 1234, once: true);

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Contains("\"s\":\"oturum1\"", l));
        Assert.Contains("\"e\":\"first-freed\"", lines[2]);
        Assert.Contains("\"v\":1234", lines[2]);
        Assert.Contains("2026-10-05T12:00:02", lines[1]);
    }

    [Fact]
    public void Olcum_Cuts_The_Older_Half_Past_One_Megabyte()
    {
        using var temp = new Temp();
        var path = temp.File("olcum.jsonl");
        var olcum = new Olcum(path, null, "oturum");
        var padding = new string('x', 1000);
        const int writes = 1500;
        for (var i = 0; i < writes; i++)
            olcum.Write(OlcumKind.Click, i, padding);

        Assert.True(new FileInfo(path).Length <= Olcum.MaxBytes);
        var lines = File.ReadAllLines(path);
        Assert.All(lines, l => Assert.StartsWith("{", l));
        Assert.Contains($"\"v\":{writes - 1},", lines[^1]);
        var firstKept = long.Parse(lines[0].Split("\"v\":")[1].Split(',')[0]);
        Assert.True(firstKept > 0);
        Assert.True(lines.Length > 300);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Olcum_Summary_Reads_The_Last_Session()
    {
        var t0 = Monday;
        string Line(string session, double seconds, string kind, long? value = null) =>
            $"{{\"t\":\"{t0.AddSeconds(seconds):O}\",\"s\":\"{session}\",\"e\":\"{kind}\"" + (value is null ? "" : $",\"v\":{value}") + "}";

        var lines = new[]
        {
            Line("eski", 0, "start"),
            Line("eski", 1, "first-card"),
            Line("eski", 5, "click"),
            "bozuk satır",
            Line("yeni", 100, "start"),
            Line("yeni", 103, "scan-done", 10),
            Line("yeni", 101.5, "first-card"),
            Line("yeni", 130, "click"),
            Line("yeni", 130, "decision"),
            Line("yeni", 131, "click"),
            Line("yeni", 131, "decision"),
            Line("yeni", 140, "first-freed", 4096),
            Line("yeni", 150, "first-freed", 9999),
            Line("yeni", 160, "undo"),
            "",
        };
        var summary = Olcum.Summarize(lines);
        Assert.NotNull(summary);
        Assert.Equal("yeni", summary.Session);
        Assert.Equal(TimeSpan.FromSeconds(1.5), summary.ToFirstCard);
        Assert.Equal(TimeSpan.FromSeconds(40), summary.ToFirstFreed);
        Assert.Equal(4096, summary.FirstFreedBytes);
        Assert.Equal(2, summary.Decisions);
        Assert.Equal(2, summary.Clicks);
        Assert.Equal(1, summary.Undos);

        Assert.Null(Olcum.Summarize(Array.Empty<string>()));
        var partial = Olcum.Summarize([Line("a", 0, "start")]);
        Assert.NotNull(partial);
        Assert.Null(partial.ToFirstCard);
        Assert.Null(partial.ToFirstFreed);
    }

    [Fact]
    public void Olcum_Summary_Reads_A_File()
    {
        using var temp = new Temp();
        var path = temp.File("olcum.jsonl");
        Assert.Null(Olcum.Summarize(path));
        var now = Monday;
        var olcum = new Olcum(path, () => now, "s1");
        olcum.Write(OlcumKind.Start);
        now = now.AddSeconds(4);
        olcum.Write(OlcumKind.FirstCard, once: true);
        now = now.AddSeconds(20);
        olcum.Write(OlcumKind.Click, null, "safe-clean");
        olcum.Write(OlcumKind.Decision, null, "safe-clean");
        olcum.Write(OlcumKind.FirstFreed, 777, once: true);
        var summary = Olcum.Summarize(path)!;
        Assert.Equal(TimeSpan.FromSeconds(4), summary.ToFirstCard);
        Assert.Equal(TimeSpan.FromSeconds(24), summary.ToFirstFreed);
        Assert.Equal((777, 1, 1), (summary.FirstFreedBytes, summary.Decisions, summary.Clicks));
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

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Ready(bool snapshot = true)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        var vm = new MainViewModel(backend);
        if (snapshot)
            vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await Settle();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Safe_Clean_Argument_Starts_The_Safe_Cluster_And_Shows_The_Report()
    {
        var (vm, backend) = await Ready();
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["--safe-clean"]));
        await Settle();

        Assert.Equal(["chrome/cache"], Assert.Single(backend.Requests, r => r.Op == Ops.Clean).Items);
        Assert.Equal(["temp"], Assert.Single(backend.Requests, r => r.Op == Ops.SystemClean).Items);
        Assert.Equal(["u4"], backend.Requests.Where(r => r.Op == Ops.Delete).Select(r => r.UnitId));
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Quarantine or Ops.Purge);
        Assert.True(vm.Tour.IsDone);
        Assert.NotNull(vm.Report);
        Assert.Null(vm.Confirm);
    }

    [AvaloniaFact]
    public async Task Safe_Clean_Waits_For_The_Running_Scan_Before_Starting()
    {
        var (vm, backend) = await Ready();
        backend.HoldScan = new TaskCompletionSource();
        var scan = vm.Session.RunScanAsync(vm, ScanMode.Full);
        await Settle();
        Assert.True(vm.Session.Scan.IsRunning);

        var launched = vm.HandleLaunchAsync(LaunchArgs.Parse(["--safe-clean"]));
        await Settle();
        Assert.False(launched.IsCompleted);
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean or Ops.Delete);

        backend.HoldScan.SetResult();
        await scan;
        await launched;
        await Settle();
        Assert.Single(backend.Requests, r => r.Op == Ops.Clean);
        Assert.True(vm.Tour.IsDone);
    }

    [AvaloniaFact]
    public async Task Safe_Clean_Scans_First_When_There_Is_No_Result_Yet()
    {
        var (vm, backend) = await Ready(snapshot: false);
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["--safe-clean"]));
        await Settle();
        Assert.NotEmpty(backend.ScanCalls);
        Assert.Single(backend.Requests, r => r.Op == Ops.Clean);
    }

    [AvaloniaFact]
    public async Task Protocol_Launch_Needs_A_Valid_Token()
    {
        var (vm, backend) = await Ready();
        var redeemed = new List<string>();
        vm.RedeemToken = t =>
        {
            redeemed.Add(t);
            return false;
        };
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["dustybytes:safe-clean?t=" + Token]));
        await Settle();
        Assert.Equal([Token], redeemed);
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean or Ops.Delete);
        Assert.Contains(vm.Toasts, t => t.Message.StartsWith("Bildirimin süresi dolmuş", StringComparison.Ordinal));

        vm.RedeemToken = t => t == Token;
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["dustybytes:safe-clean?t=" + Token]));
        await Settle();
        Assert.Single(backend.Requests, r => r.Op == Ops.Clean);
    }

    [AvaloniaFact]
    public async Task Plain_Launch_Does_Not_Clean()
    {
        var (vm, backend) = await Ready();
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["dustybytes:open"]));
        await vm.HandleLaunchAsync(new LaunchArgs(LaunchMode.Normal));
        await Settle();
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean or Ops.Delete);
    }

    [AvaloniaFact]
    public async Task Measurement_Hooks_Record_Card_Freed_Decision_And_Undo()
    {
        using var temp = new Temp();
        var olcum = new Olcum(temp.File("olcum.jsonl"), null, "kanca");
        using var scope = Olcum.Use(olcum);
        olcum.Write(OlcumKind.Start);

        var (vm, _) = await Ready();
        await vm.Overview.EstimateAsync();
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();
        await vm.Quarantine.RestoreSessionAsync("herhangi", vm.NewProgress());

        var summary = Olcum.Summarize(olcum.Path)!;
        Assert.Equal("kanca", summary.Session);
        Assert.NotNull(summary.ToFirstCard);
        Assert.NotNull(summary.ToFirstFreed);
        Assert.Equal(FakeBackend.CleanFreed * 2, summary.FirstFreedBytes);
        Assert.Equal(1, summary.Decisions);
        Assert.Equal(1, summary.Clicks);
        Assert.Equal(1, summary.Undos);
        Assert.Contains(File.ReadAllLines(olcum.Path), l => l.Contains("\"d\":\"safe-clean\"", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Notification_Launch_Counts_As_One_Decision()
    {
        using var temp = new Temp();
        var olcum = new Olcum(temp.File("olcum.jsonl"), null, "bildirim");
        using var scope = Olcum.Use(olcum);
        olcum.Write(OlcumKind.Start);

        var (vm, _) = await Ready();
        await vm.HandleLaunchAsync(LaunchArgs.Parse(["--safe-clean"]));
        await Settle();

        var summary = Olcum.Summarize(olcum.Path)!;
        Assert.Equal(1, summary.Decisions);
        Assert.Equal(1, summary.Clicks);
        Assert.True(summary.FirstFreedBytes > 0);
        Assert.Contains("notification-safe-clean", File.ReadAllText(olcum.Path));
    }
}
