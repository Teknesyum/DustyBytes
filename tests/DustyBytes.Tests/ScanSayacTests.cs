using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class ScanSayacTests
{
    const long Gb = 1L << 30;

    static Unit Make(string id, UnitKind kind, long size) =>
        new() { Id = id, Kind = kind, Name = id, Paths = [@"C:\" + id], SizeBytes = size };

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static ScanDraft Draft(params Unit[] units) => new(units, new HashSet<string>(StringComparer.Ordinal));

    static async Task<(FakeBackend Backend, MainViewModel Vm, Task Start)> Streaming()
    {
        var backend = new FakeBackend { HoldScan = new TaskCompletionSource() };
        var vm = new MainViewModel(backend);
        var start = vm.StartAsync();
        await Settle();
        Assert.NotNull(backend.DraftSink);
        return (backend, vm, start);
    }

    [Fact]
    public void Tally_Groups_Units_By_Category_And_Skips_Small_And_Other_Kinds()
    {
        var tally = ScanCounters.Tally(
        [
            Make("g1", UnitKind.Game, 40 * Gb),
            Make("g2", UnitKind.Game, 20 * Gb),
            Make("g3", UnitKind.Game, Gb - 1),
            Make("f1", UnitKind.Film, 8 * Gb),
            Make("s1", UnitKind.Series, 2 * Gb),
            Make("p1", UnitKind.Program, 3 * Gb),
            Make("c1", UnitKind.Cache, 5 * Gb),
            Make("c2", UnitKind.BrowserCache, Gb),
            Make("d1", UnitKind.Folder, 7 * Gb),
            Make("x1", UnitKind.DevArtifact, 9 * Gb),
            Make("x2", UnitKind.Duplicate, 9 * Gb),
        ]);

        Assert.Equal(ScanCounters.Categories.Count, tally.Count);
        Assert.Equal((2, 60 * Gb), tally[0]);
        Assert.Equal((2, 10 * Gb), tally[1]);
        Assert.Equal((1, 3 * Gb), tally[2]);
        Assert.Equal((2, 6 * Gb), tally[3]);
        Assert.Equal((1, 7 * Gb), tally[4]);
    }

    [Fact]
    public void Tally_Of_Nothing_Is_Zero()
    {
        Assert.All(ScanCounters.Tally([]), t => Assert.Equal((0, 0L), t));
    }

    [AvaloniaFact]
    public void Every_Category_Maps_To_The_Same_Offers_Filter()
    {
        var vm = new MainViewModel(new FakeBackend());
        foreach (var category in ScanCounters.Categories)
        {
            var filter = Assert.Single(vm.Offers.Filters, f => f.Label == category.Filter);
            Assert.Equal(category.Kinds.OrderBy(k => k), filter.Kinds.OrderBy(k => k));
        }
    }

    [AvaloniaFact]
    public async Task Counters_Fill_From_Streaming_Drafts_And_Update_In_Place()
    {
        var (backend, vm, start) = await Streaming();
        var scan = vm.Session.Scan;
        Assert.True(scan.IsRunning);
        Assert.False(scan.HasCounters);

        backend.DraftSink!.Report(Draft(Make("g1", UnitKind.Game, 40 * Gb), Make("f1", UnitKind.Film, 8 * Gb)));
        await Settle();
        Assert.True(scan.HasCounters);
        Assert.Equal(ScanCounters.Categories.Count, scan.Counters.Count);
        var games = scan.Counters[0];
        Assert.Equal("Oyunlar 1 · 40,0 GB", games.Text);
        Assert.Equal("Filmler 1 · 8,00 GB", scan.Counters[1].Text);
        Assert.Equal("Programlar 0", scan.Counters[2].Text);

        backend.DraftSink.Report(Draft(Make("g1", UnitKind.Game, 40 * Gb), Make("g2", UnitKind.Game, 20 * Gb), Make("p1", UnitKind.Program, 2 * Gb)));
        await Settle();
        Assert.Same(games, scan.Counters[0]);
        Assert.Equal(2, games.Count);
        Assert.Equal(60 * Gb, games.Bytes);
        Assert.Equal("Filmler 0", scan.Counters[1].Text);
        Assert.Equal("Programlar 1 · 2,00 GB", scan.Counters[2].Text);

        backend.HoldScan!.SetResult();
        await start;
        await Settle();
        Assert.False(scan.IsRunning);
    }

    [AvaloniaFact]
    public async Task Removed_Units_Leave_The_Counters_During_The_Scan()
    {
        var (backend, vm, start) = await Streaming();
        backend.DraftSink!.Report(Draft(Make("g1", UnitKind.Game, 40 * Gb), Make("g2", UnitKind.Game, 20 * Gb)));
        await Settle();
        Assert.Equal(2, vm.Session.Scan.Counters[0].Count);

        vm.Session.RemoveUnits(["g1"]);
        await Settle();
        Assert.Equal(1, vm.Session.Scan.Counters[0].Count);
        Assert.Equal(20 * Gb, vm.Session.Scan.Counters[0].Bytes);

        backend.HoldScan!.SetResult();
        await start;
    }

    [AvaloniaFact]
    public async Task Clicking_A_Counter_Opens_Offers_With_That_Filter()
    {
        var (backend, vm, start) = await Streaming();
        backend.DraftSink!.Report(Draft(Make("f1", UnitKind.Film, 8 * Gb), Make("g1", UnitKind.Game, 40 * Gb)));
        await Settle();
        Assert.Same(vm.Overview, vm.SelectedNav!.Screen);

        var films = vm.Session.Scan.Counters.Single(c => c.Title == "Filmler");
        films.OpenCommand.Execute(null);
        await Settle();

        Assert.Same(vm.Offers, vm.SelectedNav!.Screen);
        Assert.Equal("Film", vm.Offers.SelectedFilter.Label);
        Assert.Equal(["f1"], vm.Offers.Cards.Select(c => c.Unit.Id));

        vm.GoTo(vm.Overview);
        vm.Session.Scan.Counters.Single(c => c.Title == "Oyunlar").OpenCommand.Execute(null);
        await Settle();
        Assert.Equal("Oyun", vm.Offers.SelectedFilter.Label);
        Assert.Equal(["g1"], vm.Offers.Cards.Select(c => c.Unit.Id));

        backend.HoldScan!.SetResult();
        await start;
    }

    [AvaloniaFact]
    public async Task Counters_Reset_When_A_New_Run_Starts()
    {
        var progress = new TaskProgressViewModel();
        var gate = new TaskCompletionSource();
        var run = progress.RunAsync("Bir", async (_, _) =>
        {
            progress.SetCounters([Make("g1", UnitKind.Game, 5 * Gb)]);
            await gate.Task;
            return true;
        });
        Assert.True(progress.HasCounters);
        gate.SetResult();
        await run;

        await progress.RunAsync("İki", (_, _) => Task.FromResult(true));
        Assert.False(progress.HasCounters);
        Assert.Empty(progress.Counters);
    }

    [AvaloniaFact]
    public async Task Current_Line_Keeps_The_Newest_Path_Without_A_List()
    {
        var progress = new TaskProgressViewModel();
        var gate = new TaskCompletionSource();
        var run = progress.RunAsync("Tara", async (_, _) =>
        {
            await gate.Task;
            return true;
        });
        Assert.False(progress.HasCurrent);
        progress.AddLine(@"C:\Oyunlar", true);
        progress.AddLine(@"C:\Oyunlar\Eski", true);
        Assert.True(progress.HasCurrent);
        Assert.Equal(@"C:\Oyunlar\Eski", progress.CurrentLine);
        gate.SetResult();
        await run;
    }
}
