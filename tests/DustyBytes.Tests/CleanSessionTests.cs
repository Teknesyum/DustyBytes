using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class CleanSessionTests
{
    static MainViewModel Shell(FakeBackend backend)
    {
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        return vm;
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

    static UnitCard Card(string id) => new(FakeBackend.Snapshot().Units.First(u => u.Id == id), DateTimeOffset.Now, () => { });

    static QuarantineEntry Tagged(string id, string? session, DateTime moved, long size) =>
        FakeBackend.Entry(id, @"C:\Oyunlar\" + id, moved, size) with { SessionId = session };

    static FakeBackend Freeing()
    {
        var backend = new FakeBackend();
        backend.Respond = r =>
        {
            if (r.Op != Ops.Delete)
                return null!;
            backend.DriveFree += 3_000_000_000;
            return new WorkerResponse { Id = r.Id, Ok = true, FreedBytes = 3_000_000_000, Items = [new ItemResult(r.Paths[0], true, "", 3_000_000_000)] };
        };
        return backend;
    }

    [AvaloniaFact]
    public async Task One_Action_Stamps_One_Session_And_Reports_Before_After()
    {
        var backend = Freeing();
        var vm = Shell(backend);
        var outcome = await vm.Offers.RemoveCoreAsync([Card("u1"), Card("u4")], false, vm.NewProgress(), "Temizlik");
        await Settle();

        Assert.Null(outcome.Error);
        var quarantine = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        var delete = Assert.Single(backend.Requests, r => r.Op == Ops.Delete);
        Assert.False(string.IsNullOrEmpty(quarantine.SessionId));
        Assert.Equal(quarantine.SessionId, delete.SessionId);
        Assert.False(vm.Sessions.IsOpen);

        var report = Assert.IsType<SessionReportViewModel>(vm.Report).Report;
        Assert.Equal(quarantine.SessionId, report.Id);
        Assert.Equal(200_000_000_000, report.FreeBefore);
        Assert.Equal(203_000_000_000, report.FreeAfter);
        Assert.Equal(40_000_000_000, report.QuarantinedBytes);
        Assert.Equal(1, report.QuarantinedCount);
        Assert.Equal(3_000_000_000, report.PurgedBytes);
        Assert.Equal(1, report.PurgedCount);
        Assert.True(report.CanUndo);
        Assert.True(report.HasPurged);
        Assert.StartsWith("Önce ", report.SpaceText, StringComparison.Ordinal);
        Assert.Contains("geri alınamaz", report.PurgedText, StringComparison.Ordinal);
        Assert.True(vm.Report!.HasPurged);
    }

    [AvaloniaFact]
    public async Task Separate_Actions_Get_Separate_Sessions()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        await vm.Offers.RemoveCoreAsync([Card("u1")], false, vm.NewProgress(), "Temizlik");
        await vm.Offers.RemoveCoreAsync([Card("u3")], false, vm.NewProgress(), "Temizlik");
        await Settle();
        var ids = backend.Requests.Where(r => r.Op == Ops.Quarantine).Select(r => r.SessionId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.NotEqual(ids[0], ids[1]);
    }

    [AvaloniaFact]
    public async Task Open_Session_Spans_Several_Actions()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        var id = vm.Sessions.Begin("Tur", vm.Tour);
        await vm.Offers.RemoveCoreAsync([Card("u1")], false, vm.NewProgress(), "Temizlik");
        await vm.Offers.RemoveCoreAsync([Card("u3")], false, vm.NewProgress(), "Temizlik");
        await Settle();
        Assert.True(vm.Sessions.IsOpen);
        Assert.Null(vm.Report);
        Assert.All(backend.Requests.Where(r => r.Op == Ops.Quarantine), r => Assert.Equal(id, r.SessionId));
        var report = vm.Sessions.End();
        Assert.NotNull(report);
        Assert.Equal(2, report.QuarantinedCount);
        Assert.Equal(48_000_000_000, report.QuarantinedBytes);
        Assert.Same(report, vm.Report!.Report);
    }

    [Fact]
    public void Empty_Session_Raises_No_Report()
    {
        var sessions = new CleanSession(new FakeBackend());
        SessionReport? seen = null;
        sessions.Finished += r => seen = r;
        sessions.Begin("Temizlik");
        var report = sessions.End();
        Assert.NotNull(report);
        Assert.True(report.IsEmpty);
        Assert.Null(seen);
        Assert.Null(sessions.End());
    }

    [Fact]
    public void Scope_Joins_Open_Session_And_Only_Closes_Its_Own()
    {
        var sessions = new CleanSession(new FakeBackend());
        var id = sessions.Begin("Tur");
        using (sessions.Scope("Temizlik"))
            Assert.Equal(id, sessions.CurrentId);
        Assert.Equal(id, sessions.CurrentId);
        sessions.End();
        string? inner;
        using (sessions.Scope("Temizlik"))
            inner = sessions.CurrentId;
        Assert.NotNull(inner);
        Assert.False(sessions.IsOpen);
    }

    [AvaloniaFact]
    public async Task Leaving_The_Owner_Screen_Ends_Its_Session()
    {
        var vm = Shell(new FakeBackend());
        vm.Navigation.Push(vm.Tour);
        vm.Sessions.Begin("Tur", vm.Tour);
        vm.GoTo(vm.Quarantine);
        await Settle();
        Assert.False(vm.Sessions.IsOpen);
    }

    [AvaloniaFact]
    public async Task Report_Undo_Restores_The_Whole_Session()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        await vm.Offers.RemoveCoreAsync([Card("u1")], false, vm.NewProgress(), "Temizlik");
        await Settle();
        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == "u1");
        var report = Assert.IsType<SessionReportViewModel>(vm.Report);
        Assert.True(report.CanUndo);

        await report.UndoCommand.ExecuteAsync(null);
        await Settle();

        var restore = Assert.Single(backend.Requests, r => r.Op == Ops.Restore);
        Assert.Equal(report.Report.Id, restore.SessionId);
        Assert.Empty(restore.Items);
        Assert.Empty(restore.Paths);
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == "u1");
        Assert.Null(vm.Report);
    }

    [AvaloniaFact]
    public async Task Dry_Run_Report_Offers_No_Undo()
    {
        var backend = new FakeBackend { DryRun = true };
        var vm = Shell(backend);
        await vm.Offers.RemoveCoreAsync([Card("u1")], false, vm.NewProgress(), "Temizlik");
        await Settle();
        var report = Assert.IsType<SessionReportViewModel>(vm.Report);
        Assert.True(report.Report.DryRun);
        Assert.False(report.CanUndo);
        Assert.Contains("provası", report.Title, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Quarantine_Groups_By_Session_With_Legacy_Last()
    {
        var now = DateTime.UtcNow;
        var backend = new FakeBackend
        {
            Quarantine = new QuarantineSnapshot(
            [
                Tagged("a1", "s1", now.AddMinutes(-30), 1_000),
                Tagged("a2", "s1", now.AddMinutes(-29), 2_000),
                Tagged("b1", "s2", now.AddMinutes(-5), 4_000),
                Tagged("old", null, now.AddDays(-40), 8_000),
            ], [], true, null),
        };
        var vm = Shell(backend);
        await vm.Session.RefreshQuarantineAsync(vm);
        vm.GoTo(vm.Quarantine);
        await Settle();

        var groups = vm.Quarantine.Groups;
        Assert.Equal(["s2", "s1", null], groups.Select(g => g.SessionId));
        Assert.Equal(2, groups[1].Rows.Count);
        var day = now.AddMinutes(-5).ToLocalTime().Date == DateTime.Now.Date ? "Bugün " : "Dün ";
        Assert.StartsWith(day, groups[0].Title, StringComparison.Ordinal);
        Assert.Equal("Eski kayıtlar", groups[2].Title);
        Assert.Equal(4, vm.Quarantine.Items.Count);

        await vm.Quarantine.RestoreGroupCommand.ExecuteAsync(groups[1]);
        await Settle();
        var bySession = Assert.Single(backend.Requests, r => r.Op == Ops.Restore);
        Assert.Equal("s1", bySession.SessionId);
        Assert.Empty(bySession.Items);

        await vm.Quarantine.RestoreGroupCommand.ExecuteAsync(vm.Quarantine.Groups.Last());
        await Settle();
        var legacy = backend.Requests.Last(r => r.Op == Ops.Restore);
        Assert.Null(legacy.SessionId);
        Assert.Equal(["old"], legacy.Items);
    }

    [Fact]
    public void Group_Title_Names_Today_And_Yesterday()
    {
        var now = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Local);
        Assert.Equal("Bugün 14:02", QuarantineViewModel.GroupTitle(new DateTime(2026, 10, 2, 14, 2, 0, DateTimeKind.Local).ToUniversalTime(), now));
        Assert.Equal("Dün 09:30", QuarantineViewModel.GroupTitle(new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Local).ToUniversalTime(), now));
        Assert.Equal("28 Eylül 08:00", QuarantineViewModel.GroupTitle(new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Local).ToUniversalTime(), now));
    }

    [AvaloniaFact]
    public async Task Empty_All_Disarms_When_Leaving_The_Screen()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend);
        await vm.Session.RefreshQuarantineAsync(vm);
        vm.GoTo(vm.Quarantine);
        await Settle();
        await vm.Quarantine.EmptyAllCommand.ExecuteAsync(null);
        Assert.True(vm.Quarantine.IsEmptyArmed);
        vm.GoTo(vm.Overview);
        await Settle();
        Assert.False(vm.Quarantine.IsEmptyArmed);
        vm.GoTo(vm.Quarantine);
        await Settle();
        await vm.Quarantine.EmptyAllCommand.ExecuteAsync(null);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Purge);
    }
}
