using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class SurucuSecimiTests
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

    static MainViewModel Shell(FakeBackend backend)
    {
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.TwoDrives());
        return vm;
    }

    [AvaloniaFact]
    public void Chips_Start_On_All_With_Each_Drive()
    {
        var overview = Shell(new FakeBackend()).Overview;

        Assert.True(overview.HasDrives);
        Assert.Equal(["Tümü", "C: Sistem", "D: Oyun"], overview.Drives.Select(d => d.Label));
        Assert.Null(overview.SelectedDrive!.Root);
        Assert.Equal($"{Format.Bytes(100_000_000_000)} boş / {Format.Bytes(500_000_000_000)}", overview.Drives[1].Detail);
        Assert.Equal(0.8, overview.Drives[1].Share, 3);
        Assert.Equal("6 birim", overview.UnitCountText);
        Assert.Equal("1.500 dosya tarandı", overview.ScannedText);
        Assert.Contains(overview.Top, t => t.KindLabel == "Oyun · D:");
    }

    [AvaloniaFact]
    public void Picking_A_Drive_Filters_Overview_And_Map()
    {
        var vm = Shell(new FakeBackend());
        var overview = vm.Overview;

        overview.SelectedDrive = overview.Drives.Single(d => d.Root == @"D:\");

        Assert.Equal(@"D:\", vm.Session.SelectedDrive);
        Assert.Equal("1 birim", overview.UnitCountText);
        Assert.Equal("500 dosya tarandı", overview.ScannedText);
        Assert.Equal("Büyük Oyun", Assert.Single(overview.Top).Name);
        Assert.Equal("Oyun", overview.Top[0].KindLabel);
        Assert.Equal(@"D:\", vm.Map.Crumbs[0].Node.Name);
        Assert.Equal(UnitKind.Game, vm.Map.Entries.Single(e => e.Name == "Oyunlar").Kind);

        overview.SelectedDrive = overview.Drives[0];

        Assert.Null(vm.Session.SelectedDrive);
        Assert.Equal("6 birim", overview.UnitCountText);
        Assert.Equal(@"C:\", vm.Map.Crumbs[0].Node.Name);
    }

    [AvaloniaFact]
    public void Single_Drive_Shows_No_Chips_And_Selection_Resets_When_Drive_Goes()
    {
        var vm = Shell(new FakeBackend());
        vm.Overview.SelectedDrive = vm.Overview.Drives.Single(d => d.Root == @"D:\");

        vm.Session.SetSnapshot(FakeBackend.Snapshot());

        Assert.Null(vm.Session.SelectedDrive);
        Assert.False(vm.Overview.HasDrives);
        Assert.Equal("5 birim", vm.Overview.UnitCountText);
    }

    [AvaloniaFact]
    public async Task Removable_Toggle_Is_Saved_And_Refreshes()
    {
        var backend = new FakeBackend { Refreshed = FakeBackend.TwoDrives() };
        var vm = Shell(backend);

        vm.Overview.ScanRemovable = true;
        for (var i = 0; i < 10 && backend.ScanCalls.Count == 0; i++)
        {
            Pump();
            await Task.Delay(5);
        }

        Assert.True(backend.ScanRemovable);
        Assert.Equal("refresh", Assert.Single(backend.ScanCalls));
        Assert.Equal(2, backend.RefreshedFrom!.Results.Count);
    }

    [AvaloniaFact]
    public void Overview_Window_Renders_Drive_Chips()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.Session.SetSnapshot(FakeBackend.TwoDrives());
        vm.GoTo(vm.Overview);
        Pump();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DriveFilter");
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(3, list.GetVisualDescendants().OfType<ListBoxItem>().Count());
        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "RemovableToggle");
        window.Close();
    }
}
