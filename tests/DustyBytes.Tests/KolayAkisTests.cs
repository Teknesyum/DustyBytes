using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class KolayAkisTests
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

    static async Task<(MainWindow Window, MainViewModel Vm)> OffersWindow()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        for (var i = 0; i < 10; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        return (window, vm);
    }

    static Border Row(MainWindow window, string id) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row") && b.DataContext is UnitCard c && c.Unit.Id == id);

    static void Click(MainWindow window, Visual target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public async Task Row_Press_Toggles_Selection_But_Buttons_Do_Not()
    {
        var (window, vm) = await OffersWindow();
        var row = Row(window, "u1");
        var card = (UnitCard)row.DataContext!;
        var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == card.Name);

        Click(window, name);
        Assert.True(card.IsSelected);
        Click(window, name);
        Assert.False(card.IsSelected);

        var purge = row.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("purge"));
        Click(window, purge);
        Assert.False(card.IsSelected);
        Assert.True(card.IsPurgeArmed);
        Assert.Contains("danger", purge.Classes);

        Click(window, name);
        Assert.False(card.IsPurgeArmed);
        Assert.True(card.IsSelected);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Select_All_Box_Selects_Then_Clears()
    {
        var (window, vm) = await OffersWindow();
        var box = window.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "SelectAll");
        Assert.True(box.IsEffectivelyVisible);
        Assert.False(box.IsChecked);

        Click(window, box);
        Assert.True(vm.Offers.AllSelected);
        Assert.True(box.IsChecked);

        ((UnitCard)Row(window, "u3").DataContext!).IsSelected = false;
        Pump();
        Assert.Null(box.IsChecked);

        Click(window, box);
        Assert.True(box.IsChecked);
        Click(window, box);
        Assert.False(box.IsChecked);
        Assert.Empty(vm.Offers.Chosen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Row_Is_One_Line_With_Path_In_Tooltip()
    {
        var (window, _) = await OffersWindow();
        var row = Row(window, "u1");
        Assert.True(row.Bounds.Height < 2 * (double)window.FindResource("InputHeight")!, row.Bounds.Height.ToString());
        Assert.IsType<StackPanel>(ToolTip.GetTip(row));
        var box = row.GetVisualDescendants().OfType<CheckBox>().First();
        Assert.True(box.Bounds.Width >= (double)window.FindResource("InputHeight")!);
        window.Close();
    }

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Toured(bool dryRun = false)
    {
        var backend = FakeBackend.Rich();
        backend.DryRun = dryRun;
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.StartTourAsync();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Auto_Step_Cleans_Only_Categories_That_Need_No_Question()
    {
        var (vm, backend) = await Toured();
        Assert.Null(vm.Confirm);
        Assert.Same(vm.Tour, vm.Navigation.Current);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal(["u4"], backend.Requests.Where(r => r.Op == Ops.Delete).Select(r => r.UnitId));
        Assert.Equal(["chrome/cache"], Assert.Single(backend.Requests, r => r.Op == Ops.Clean).Items);
        Assert.Equal(["temp"], Assert.Single(backend.Requests, r => r.Op == Ops.SystemClean).Items);
        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == "u4");
        Assert.True(vm.Tour.IsAsking);
    }

    [AvaloniaFact]
    public async Task Tour_Walks_Large_Old_Units_In_Offer_Order_And_Warns_On_User_Data()
    {
        var (vm, backend) = await Toured();
        var tour = vm.Tour;
        Assert.Equal(["u1", "u3"], tour.Items.Select(c => c.Unit.Id));
        Assert.Equal("u1", tour.Current!.Unit.Id);
        Assert.Equal("1 / 2", tour.StepText);
        Assert.False(tour.HasUserData);
        Assert.Equal("Bunu silmek ister misiniz?", tour.Question);

        tour.KeepCommand.Execute(null);
        Assert.Equal("u3", tour.Current!.Unit.Id);
        Assert.Equal("2 / 2", tour.StepText);
        Assert.True(tour.HasUserData);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId is "u1" or "u3");

        await tour.QuarantineCommand.ExecuteAsync(null);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("u3", request.UnitId);
        Assert.True(request.UserApproved);
        Assert.True(tour.IsDone);
        Assert.True(tour.HasQuarantined);
        Assert.Contains(tour.Summary!.Lines, l => l.StartsWith("Karantinaya alınan: 1 öğe", StringComparison.Ordinal));
        Assert.Contains(tour.Summary.Lines, l => l == "Yerinde kalan: 1 öğe");
        Assert.Equal(3_000_000_000 + 8_000_000_000, tour.FreedBytes);

        tour.UndoCommand.Execute(null);
        Assert.Same(vm.Quarantine, vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Tour_Purge_Needs_Two_Presses_And_Expires()
    {
        var (vm, backend) = await Toured();
        var tour = vm.Tour;
        await tour.PurgeOneCommand.ExecuteAsync(null);
        Assert.True(tour.IsPurgeArmed);
        Assert.Equal(TwoStep.ArmedText, tour.PurgeText);
        vm.Tick(TimeSpan.FromSeconds(4.5));
        Assert.False(tour.IsPurgeArmed);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == "u1");

        await tour.PurgeOneCommand.ExecuteAsync(null);
        await tour.PurgeOneCommand.ExecuteAsync(null);
        var request = Assert.Single(backend.Requests, r => r.UnitId == "u1");
        Assert.Equal(Ops.Delete, request.Op);
        Assert.Equal("u3", tour.Current!.Unit.Id);
        Assert.False(tour.IsPurgeArmed);
        Assert.Equal(3_000_000_000 + 40_000_000_000, tour.FreedBytes);
    }

    [AvaloniaFact]
    public async Task Tour_Ends_Early_And_Returns()
    {
        var (vm, backend) = await Toured();
        vm.Tour.EndCommand.Execute(null);
        Assert.True(vm.Tour.IsDone);
        Assert.False(vm.Tour.HasQuarantined);
        Assert.Contains(vm.Tour.Summary!.Lines, l => l == "Yerinde kalan: 2 öğe");
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);
        vm.Tour.CloseCommand.Execute(null);
        Assert.Same(vm.Overview, vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Tour_Dry_Run_Keeps_Units()
    {
        var (vm, _) = await Toured(dryRun: true);
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == "u4");
        await vm.Tour.QuarantineCommand.ExecuteAsync(null);
        await vm.Tour.QuarantineCommand.ExecuteAsync(null);
        Assert.True(vm.Tour.Summary!.IsDryRun);
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == "u1");
    }

    [AvaloniaFact]
    public void Tour_Skips_Recent_Small_And_External_Units()
    {
        var now = DateTimeOffset.Now;
        Unit Make(string id, long size, int days, RemovalMethod removal = RemovalMethod.Quarantine) => new()
        {
            Id = id, Name = id, Kind = UnitKind.Folder, Paths = [@"C:\" + id], SizeBytes = size, Removal = removal,
            Usage = new UsageSignal(now.AddDays(-days), "prefetch", 0.9),
        };
        var picked = TourViewModel.Pick(
        [
            Make("recent", 50_000_000_000, 10),
            Make("small", 100_000_000, 400),
            Make("launcher", 50_000_000_000, 400, RemovalMethod.Launcher),
            Make("old", 5_000_000_000, 400),
        ], now);
        Assert.Equal(["old"], picked.Select(u => u.Id));
    }

    [AvaloniaFact]
    public async Task Auto_Button_Opens_Tour_Without_A_Window()
    {
        var (window, vm) = await OffersWindow();
        var auto = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "AutoButton" && b.IsEffectivelyVisible);
        Assert.Contains("primary", auto.Classes);
        Click(window, auto);
        for (var i = 0; i < 10; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        Assert.Same(vm.Tour, vm.Navigation.Current);
        Assert.Null(vm.Confirm);
        var card = window.GetVisualDescendants().OfType<TransitioningContentControl>().First(c => c.Name == "Card");
        Assert.Same(vm.Tour.Current, card.Content);
        var purge = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "TourPurge");
        Click(window, purge);
        Assert.Contains("danger", purge.Classes);
        Click(window, window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "FreedText"));
        Assert.False(vm.Tour.IsPurgeArmed);
        window.Close();
    }

    static async Task<(MainWindow Window, MainViewModel Vm, TransitioningContentControl Card)> TourWindow()
    {
        var (window, vm) = await OffersWindow();
        await vm.StartTourAsync();
        for (var i = 0; i < 10; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        return (window, vm, window.GetVisualDescendants().OfType<TransitioningContentControl>().First(c => c.Name == "Card"));
    }

    [AvaloniaFact]
    public async Task Tour_Card_Enters_With_Motion_Tokens_And_Stays_Visible()
    {
        var (window, vm, card) = await TourWindow();
        var motion = Assert.IsType<DustyBytes.App.Views.EntryTransition>(card.PageTransition);
        Assert.Equal((TimeSpan)window.FindResource("TBase")!, motion.Duration);
        Assert.Equal((double)window.FindResource("EntryOffset")!, motion.Offset);

        vm.Tour.KeepCommand.Execute(null);
        for (var i = 0; i < 20; i++)
        {
            Pump();
            await Task.Delay(20);
        }
        var name = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Film" && t.IsEffectivelyVisible);
        Assert.All(name.GetSelfAndVisualAncestors().TakeWhile(v => v != card), v => Assert.Equal(1, v.Opacity));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Tour_Card_Has_No_Motion_When_Reduced()
    {
        var before = Environment.GetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", "1");
            var (window, _, card) = await TourWindow();
            Assert.Null(card.PageTransition);
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", before);
        }
    }
}
