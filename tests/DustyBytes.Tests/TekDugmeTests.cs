using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class TekDugmeTests
{
    const long SafeTotal = 3_000_000_000 + 1_000_000 + 120_000_000;

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Ready(Action<FakeBackend>? setup = null)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        setup?.Invoke(backend);
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await vm.Overview.EstimateAsync();
        await Settle();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Buttons_Stay_Disabled_While_Scanning()
    {
        var (vm, backend) = await Ready();
        var overview = vm.Overview;
        Assert.True(overview.SafeCleanCommand.CanExecute(null));
        Assert.True(overview.AutoCleanCommand.CanExecute(null));

        backend.HoldScan = new TaskCompletionSource();
        var scan = vm.Session.RunScanAsync(vm, ScanMode.Full);
        await Settle();
        Assert.True(overview.IsScanning);
        Assert.True(overview.ShowSafe);
        Assert.False(overview.SafeCleanCommand.CanExecute(null));
        Assert.False(overview.AutoCleanCommand.CanExecute(null));
        Assert.False(overview.MoreSpaceCommand.CanExecute(null));
        Assert.Equal(OverviewViewModel.WaitText, overview.SafeReason);
        Assert.Equal("Tarama bitince açılır", overview.AutoTip);
        Assert.False(overview.IsSafeEmpty);

        await vm.StartTourAsync(TourMode.Safe);
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean or Ops.Delete);

        backend.HoldScan.SetResult();
        await scan;
        await Settle();
        Assert.False(overview.IsScanning);
        Assert.True(overview.SafeCleanCommand.CanExecute(null));
        Assert.True(overview.AutoCleanCommand.CanExecute(null));
        Assert.False(overview.HasSafeReason);
    }

    [AvaloniaFact]
    public async Task Safe_Total_Counts_Only_What_Needs_No_Question()
    {
        var (vm, _) = await Ready();
        var overview = vm.Overview;
        Assert.True(overview.IsSafeKnown);
        Assert.Equal(SafeTotal, overview.SafeBytes);
        Assert.Equal($"Güvenle silinebilir: {Format.Bytes(SafeTotal)} — Temizle", overview.SafeText);
        Assert.True(overview.HasMore);
        Assert.Equal($"Daha fazla yer: {Format.Bytes(48_000_000_000)}, 2 karar →", overview.MoreText);
    }

    [AvaloniaFact]
    public async Task Safe_Clean_Runs_Only_The_Silent_Half_And_Counts_Real_Bytes()
    {
        var (vm, backend) = await Ready();
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        Assert.Null(vm.Confirm);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal(["chrome/cache"], Assert.Single(backend.Requests, r => r.Op == Ops.Clean).Items);
        Assert.Equal(["temp"], Assert.Single(backend.Requests, r => r.Op == Ops.SystemClean).Items);
        Assert.Equal(["u4"], backend.Requests.Where(r => r.Op == Ops.Delete).Select(r => r.UnitId));

        var tour = vm.Tour;
        Assert.True(tour.IsDone);
        var real = FakeBackend.CleanFreed * 2 + FakeBackend.DeleteFreed;
        Assert.Equal(real, tour.FreedBytes);
        Assert.Equal(0, tour.HeldBytes);
        Assert.Equal($"{Format.Bytes(real)} boşaldı", tour.Summary!.FreedText);
        Assert.Equal(real, vm.Overview.NowFreedBytes);
        Assert.StartsWith($"Şimdi boşalan {Format.Bytes(real)} · Karantinada ", vm.Overview.CounterText);
    }

    [AvaloniaFact]
    public async Task More_Space_Asks_Without_Cleaning_And_Counts_Quarantine_Apart()
    {
        var (vm, backend) = await Ready();
        await vm.Overview.MoreSpaceCommand.ExecuteAsync(null);
        await Settle();

        var tour = vm.Tour;
        Assert.True(tour.IsAsking);
        Assert.Equal("u1", tour.Current!.Unit.Id);
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean or Ops.Delete);

        await tour.QuarantineCommand.ExecuteAsync(null);
        Assert.Equal(0, tour.FreedBytes);
        Assert.Equal(FakeBackend.QuarantinePending, tour.HeldBytes);
        tour.EndCommand.Execute(null);
        Assert.DoesNotContain(tour.Summary!.Lines, l => l.StartsWith("Önbellek", StringComparison.Ordinal));
        Assert.Contains(tour.Summary.Lines, l => l.StartsWith("Karantinaya alınan: 1 öğe", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Freed_Counter_Comes_From_Worker_Not_Estimates()
    {
        var (vm, backend) = await Ready();
        var overview = vm.Overview;
        Assert.Equal(0, overview.NowFreedBytes);
        Assert.StartsWith($"Şimdi boşalan {Format.Bytes(0)} · Karantinada {Format.Bytes(6_000_000_000)}", overview.CounterText);

        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, FreedBytes = 1_234_567 };
        await overview.FreeNowCommand.ExecuteAsync(null);
        await overview.FreeNowCommand.ExecuteAsync(null);
        await Settle();
        Assert.Single(backend.Requests, r => r.Op == Ops.Purge);
        Assert.Equal(1_234_567, overview.NowFreedBytes);
    }

    [AvaloniaFact]
    public async Task Empty_Safe_Set_Says_So_Without_An_Error()
    {
        var (vm, _) = await Ready(b =>
        {
            b.Rules = [];
            b.Tasks = [];
        });
        vm.Session.RemoveUnits(["u4"]);
        await vm.Overview.EstimateAsync();
        await Settle();

        var overview = vm.Overview;
        Assert.True(overview.IsSafeEmpty);
        Assert.False(overview.ShowSafeButton);
        Assert.False(overview.SafeCleanCommand.CanExecute(null));
        Assert.Equal("Güvenle silinecek bir şey kalmadı", OverviewViewModel.SafeEmptyText);
        Assert.False(overview.HasError);
        Assert.True(overview.HasMore);
    }

    [AvaloniaFact]
    public void Held_Space_Groups_By_Drive_And_Reports_First_Due_Day()
    {
        var now = DateTime.UtcNow;
        QuarantineEntry Entry(string id, string path, int daysAgo, long size) => new()
        {
            Id = id,
            OriginalPath = path,
            Root = path[..3] + "$DustyBytes",
            Size = size,
            MovedUtc = now.AddDays(-daysAgo),
            ExpiresUtc = now.AddDays(30 - daysAgo),
        };
        Assert.Null(DiskCheck.Held([], now));
        var held = DiskCheck.Held([Entry("a", @"C:\Eski", 1, 4), Entry("b", @"D:\Arşiv", 3, 6), Entry("c", @"c:\Film", 1, 2)], now)!;
        Assert.Equal(12, held.Bytes);
        Assert.Equal(3, held.Count);
        Assert.False(held.SameDay);
        Assert.Equal(DiskCheck.DaysLeft(Entry("b", @"D:\Arşiv", 3, 6), now), held.DaysLeft);
        Assert.Equal(2, held.Roots.Count);
        Assert.Equal(["a", "c"], held.Roots.Single(r => r.Root!.StartsWith("C", StringComparison.OrdinalIgnoreCase)).Ids);
    }
}
