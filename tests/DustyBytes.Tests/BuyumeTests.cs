using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Scan;

namespace DustyBytes.Tests;

public class BuyumeTests
{
    const long GB = 1L << 30;
    static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    sealed class Notifier : INotifier
    {
        public List<(string Title, string Body)> Shown { get; } = [];

        public bool Show(string title, string body, string launch)
        {
            Shown.Add((title, body));
            return true;
        }
    }

    static FolderGrowth Growth(long total, int daysAgo, params (string Path, long Bytes)[] top) =>
        new(total, [.. top.Select(t => new FolderGrowthItem(t.Path, t.Bytes))], Now.AddDays(-daysAgo));

    static async Task<MainViewModel> Shell(FakeBackend backend, ScanSnapshot? snapshot = null)
    {
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(snapshot ?? FakeBackend.Snapshot());
        await vm.Overview.Growth.Loading;
        return vm;
    }

    [AvaloniaFact]
    public async Task Card_Shows_Total_And_Top_Folders()
    {
        var backend = new FakeBackend
        {
            Growth = _ => Growth(14 * GB, 1, (@"C:\Oyunlar\Eski Oyun", 9 * GB), (@"C:\Kod\node_modules", 3 * GB), (@"C:\Videolar\Film", 2 * GB)),
        };
        var growth = (await Shell(backend)).Overview.Growth;

        Assert.True(growth.HasGrowth);
        Assert.Equal("Son taramadan beri +14,0 GB · en çok büyüyenler", growth.Title);
        Assert.Equal(["Eski Oyun", "node_modules", "Film"], growth.Rows.Select(r => r.Name));
        Assert.Equal("+9,00 GB", growth.Rows[0].SizeText);
        Assert.Equal(@"C:\Oyunlar\Eski Oyun", growth.Rows[0].Path);
    }

    [AvaloniaFact]
    public async Task Card_Stays_Hidden_Without_Growth()
    {
        var growth = (await Shell(new FakeBackend())).Overview.Growth;

        Assert.False(growth.HasGrowth);
        Assert.Empty(growth.Rows);
        Assert.Equal("", growth.Title);
    }

    [AvaloniaFact]
    public async Task Card_Hides_Again_When_Next_Scan_Has_No_Growth()
    {
        var backend = new FakeBackend { Growth = _ => Growth(5 * GB, 1, (@"C:\A", 5 * GB)) };
        var vm = await Shell(backend);
        Assert.True(vm.Overview.Growth.HasGrowth);

        backend.Growth = _ => null;
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        await vm.Overview.Growth.Loading;

        Assert.False(vm.Overview.Growth.HasGrowth);
        Assert.Empty(vm.Overview.Growth.Rows);
    }

    [AvaloniaFact]
    public async Task Inspect_Opens_The_Folder_On_The_Map()
    {
        var backend = new FakeBackend { Growth = _ => Growth(9 * GB, 1, (@"C:\Oyunlar\Eski Oyun", 9 * GB)) };
        var vm = await Shell(backend);

        vm.Overview.Growth.InspectCommand.Execute(vm.Overview.Growth.Rows[0]);

        Assert.Same(vm.Map, vm.Navigation.Current);
        Assert.Equal(@"C:\Oyunlar\Eski Oyun", vm.Map.FocusPath);
        Assert.Equal("Eski Oyun", vm.Map.Crumbs[^1].Name);
    }

    [AvaloniaFact]
    public async Task Growth_Is_Computed_Once_Per_Scan_Result()
    {
        var backend = new FakeBackend { Growth = _ => Growth(5 * GB, 1, (@"C:\A", 5 * GB)) };
        var vm = await Shell(backend);
        Assert.Single(backend.GrowthCalls);

        vm.Session.RemoveUnits(["u5"]);
        await vm.Overview.Growth.Loading;

        Assert.Single(backend.GrowthCalls);
    }

    [AvaloniaFact]
    public async Task Picking_A_Drive_Narrows_The_Card_To_That_Drive()
    {
        var backend = new FakeBackend
        {
            Growth = r => r.Root.Name == @"D:\"
                ? Growth(7 * GB, 1, (@"D:\Oyunlar", 7 * GB))
                : Growth(3 * GB, 1, (@"C:\Kod", 3 * GB)),
        };
        var vm = await Shell(backend, FakeBackend.TwoDrives());
        Assert.Equal(["Oyunlar", "Kod"], vm.Overview.Growth.Rows.Select(r => r.Name));
        Assert.Contains("+10,0 GB", vm.Overview.Growth.Title);

        vm.Session.SelectedDrive = @"D:\";
        await vm.Overview.Growth.Loading;

        Assert.Equal("Oyunlar", Assert.Single(vm.Overview.Growth.Rows).Name);
        Assert.Contains("+7,00 GB", vm.Overview.Growth.Title);
    }

    [AvaloniaFact]
    public void Overview_Draws_The_Card_Only_When_There_Is_Growth()
    {
        var window = new MainWindow();
        window.Show();
        var vm = (MainViewModel)window.DataContext!;
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        vm.SelectedNav = vm.NavItems[0];
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "GrowthCard");
        Assert.True(card.IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "İncele");
        window.Close();
    }

    [Fact]
    public void Weekly_Sentence_Names_The_Folders()
    {
        Assert.Equal("Geçen haftadan beri +12,0 GB: Downloads", GrowthText.Weekly(Growth(12 * GB, 7, (@"C:\Users\ben\Downloads", 12 * GB)), Now));
        Assert.Equal("Geçen haftadan beri +20,0 GB: Downloads, Steam",
            GrowthText.Weekly(Growth(20 * GB, 6, (@"C:\Users\ben\Downloads", 12 * GB), (@"D:\Steam", 8 * GB)), Now));
        Assert.Equal("Son 3 günde +5,00 GB: Kod", GrowthText.Weekly(Growth(5 * GB, 3, (@"C:\Kod", 5 * GB)), Now));
    }

    [Fact]
    public void Notification_Body_Carries_The_Growth_Sentence()
    {
        var notifier = new Notifier();
        var drives = new[] { new DriveSpace(@"C:\", 500 * GB, 40 * GB), new DriveSpace(@"D:\", 500 * GB, 20 * GB) };

        Assert.Equal(0, DiskCheck.Run(drives, notifier, root => root == @"D:\" ? "Geçen haftadan beri +12,0 GB: Downloads" : null));

        var shown = Assert.Single(notifier.Shown);
        Assert.Equal("Geçen haftadan beri +12,0 GB: Downloads. DustyBytes ile yer açın", shown.Body);
    }

    [Fact]
    public void Notification_Body_Is_Unchanged_Without_Growth()
    {
        var notifier = new Notifier();
        var drive = new[] { new DriveSpace(@"C:\", 500 * GB, 40 * GB) };

        DiskCheck.Run(drive, notifier, _ => null);
        DiskCheck.Run(drive, notifier);

        Assert.All(notifier.Shown, s => Assert.Equal(DiskCheck.Body, s.Body));
        Assert.Equal(2, notifier.Shown.Count);
    }
}
