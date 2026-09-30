using Avalonia;
using Avalonia.Automation;
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

public class YerAcTests
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

    static async Task Settle()
    {
        for (var i = 0; i < 15; i++)
        {
            Pump();
            await Task.Delay(5);
        }
    }

    static async Task<(MainWindow Window, MainViewModel Vm, FakeBackend Backend)> OffersWindow()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        await Settle();
        return (window, vm, (FakeBackend)vm.Backend);
    }

    static Border Row(MainWindow window, string id) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row") && b.DataContext is UnitCard c && c.Unit.Id == id);

    static Button? Action(Border row, string name) =>
        row.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && (b.Content as string == name || AutomationProperties.GetName(b) == name));

    static async Task Click(MainWindow window, Visual target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        await Settle();
    }

    [AvaloniaFact]
    public async Task Compress_Shows_Estimate_Then_Runs_And_Offers_Undo()
    {
        var (window, vm, backend) = await OffersWindow();
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, FreedBytes = 2L << 30 };
        var row = Row(window, "u1");
        var card = (UnitCard)row.DataContext!;
        var compress = Action(row, "Küçült");
        Assert.NotNull(compress);
        Assert.Null(Action(row, "Küçültmeyi geri al"));
        Assert.Null(Action(Row(window, "u3"), "Küçült"));
        Assert.Null(Action(Row(window, "u4"), "Küçült"));

        await Click(window, compress);
        Assert.True(card.IsCompressArmed);
        Assert.Equal($"≈{Format.Bytes(3L << 30)} kazanç, onayla", compress.Content);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Compress);
        Assert.False(card.IsSelected);

        await Click(window, compress);
        var sent = Assert.Single(backend.Requests, r => r.Op == Ops.Compress);
        Assert.True(sent.UserApproved);
        Assert.Equal([@"C:\Oyunlar\Eski Oyun"], sent.Paths);

        row = Row(window, "u1");
        var undo = Action(row, "Küçültmeyi geri al");
        Assert.NotNull(undo);
        Assert.Null(Action(row, "Küçült"));
        Assert.Equal(40_000_000_000 - (2L << 30), ((UnitCard)row.DataContext!).Unit.SizeBytes);
        Assert.Contains(vm.Toasts, t => t.ActionText == "Küçültmeyi geri al");
        Assert.True(row.Bounds.Height < 2 * (double)window.FindResource("InputHeight")!, row.Bounds.Height.ToString());

        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, PendingBytes = 2L << 30 };
        await Click(window, undo);
        Assert.Single(backend.Requests, r => r.Op == Ops.Uncompress);
        Assert.NotNull(Action(Row(window, "u1"), "Küçült"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Small_Gain_Is_Not_Offered()
    {
        var (window, vm, backend) = await OffersWindow();
        backend.Estimate = new(1L << 30, 10L << 20, 50, 20);
        var row = Row(window, "u2");
        await Click(window, Action(row, "Küçült")!);

        Assert.False(((UnitCard)row.DataContext!).IsCompressArmed);
        Assert.Contains(vm.Toasts, t => t.Message.Contains("küçültmeye değmez"));
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Compress);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Cloud_Copy_Frees_Then_Keeps_On_Device()
    {
        var (window, vm, backend) = await OffersWindow();
        var snap = FakeBackend.Snapshot();
        var cloud = new Unit
        {
            Id = "u6",
            Kind = UnitKind.CloudCopy,
            Name = "Arşiv",
            Paths = [@"C:\Users\ali\OneDrive\Arşiv\eski.zip"],
            SizeBytes = 2_000_000_000,
            Removal = RemovalMethod.CloudOnly,
            ContainsUserData = true,
            Score = 6,
        };
        vm.Session.SetSnapshot(snap with { Units = [.. snap.Units, cloud] });
        await Settle();
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, FreedBytes = 2_000_000_000 };

        var row = Row(window, "u6");
        Assert.Null(Action(row, "Küçült"));
        Assert.Null(Action(row, "Karantinaya al"));
        var free = Action(row, "Yalnız çevrimiçi yap");
        Assert.NotNull(free);
        Assert.Null(Action(row, "Bu cihazda tut"));
        Assert.Contains("bulutta kalır", ToolTip.GetTip(free) as string);

        await Click(window, free);
        var sent = Assert.Single(backend.Requests, r => r.Op == Ops.CloudFree);
        Assert.True(sent.IncludeUserData);
        Assert.True(sent.UserApproved);
        Assert.Contains(vm.Toasts, t => t.ActionText == "Bu cihazda tut" && t.Message.Contains("bulutta"));

        row = Row(window, "u6");
        var keep = Action(row, "Bu cihazda tut");
        Assert.NotNull(keep);
        Assert.Null(Action(row, "Yalnız çevrimiçi yap"));
        Assert.True(row.Bounds.Height < 2 * (double)window.FindResource("InputHeight")!, row.Bounds.Height.ToString());

        await Click(window, keep);
        Assert.Single(backend.Requests, r => r.Op == Ops.CloudKeep);
        Assert.NotNull(Action(Row(window, "u6"), "Yalnız çevrimiçi yap"));
        window.Close();
    }
}
