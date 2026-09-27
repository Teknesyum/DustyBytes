using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.ViewModels;

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
}
