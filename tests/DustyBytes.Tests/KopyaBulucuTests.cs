using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.App;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Scan.Duplicates;
using DustyBytes.Units;

namespace DustyBytes.Tests;

public class KopyaBulucuTests
{
    static readonly Unit Films = DuplicateUnits.Build(new DuplicateGroup("0123456789ABCDEF0123456789ABCDEF", 3_000_000_000,
    [
        new DuplicateCopy(@"C:\Users\alice\Downloads\film.mkv", 3_000_000_000, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), default),
        new DuplicateCopy(@"D:\Arşiv\film.mkv", 3_000_000_000, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), default),
        new DuplicateCopy(@"E:\Yedek\film.mkv", 3_000_000_000, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), default),
    ]));

    static readonly Unit Photos = DuplicateUnits.Build(new DuplicateGroup("FEDCBA9876543210FEDCBA9876543210", 20_000_000,
    [
        new DuplicateCopy(@"C:\Users\alice\Pictures\tatil.zip", 20_000_000, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero), default),
        new DuplicateCopy(@"C:\Users\alice\Desktop\tatil.zip", 20_000_000, new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), default),
    ]));

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<MainViewModel> Found(FakeBackend backend)
    {
        SessionState.DuplicateFlushDelay = TimeSpan.FromMilliseconds(1);
        backend.Duplicates = [Films, Photos];
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.Session.FindDuplicates();
        await vm.Session.DuplicateSearch;
        await Settle();
        return vm;
    }

    [AvaloniaFact]
    public async Task Tarama_Bitince_Kopyalar_Arka_Planda_Aranir_Ve_Oneriye_Eklenir()
    {
        SessionState.DuplicateFlushDelay = TimeSpan.FromMilliseconds(1);
        var backend = new FakeBackend { Fresh = FakeBackend.Snapshot(), Duplicates = [Films] };
        var vm = new MainViewModel(backend);

        await vm.Session.RunScanAsync(vm, ScanMode.Full);
        await vm.Session.DuplicateSearch;
        await Settle();

        Assert.Equal(1, backend.DuplicateCalls);
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == Films.Id);
        Assert.False(vm.Session.IsFindingDuplicates);
        Assert.Contains("1 kopya grubu", vm.Session.DuplicateStatus);
    }

    [AvaloniaFact]
    public async Task Kart_Kalacak_Kopyayi_Isaretler_Ve_Degistirilebilir()
    {
        var backend = new FakeBackend();
        var vm = await Found(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.SelectedFilter = vm.Offers.Filters.First(f => f.Label == "Kopyalar");
        await Settle();

        Assert.Equal(2, vm.Offers.Cards.Count);
        var card = vm.Offers.Cards.First(c => c.Unit.Id == Films.Id);
        Assert.True(card.IsDuplicate);
        Assert.Equal(@"D:\Arşiv\film.mkv", card.Copies.Single(c => c.IsKept).Path);
        Assert.Equal(2, card.Copies.Count(c => c.IsOffered));
        Assert.Contains("en eski", card.KeepRule);
        Assert.Equal("Kopya", card.KindLabel);

        card.Copies.First(c => c.Path == @"E:\Yedek\film.mkv").KeepCommand.Execute(null);

        Assert.Equal(@"E:\Yedek\film.mkv", card.Unit.Keep);
        Assert.Equal(@"E:\Yedek\film.mkv", card.Copies.Single(c => c.IsKept).Path);
        Assert.Contains(@"D:\Arşiv\film.mkv", card.Unit.Paths);
        Assert.DoesNotContain(@"E:\Yedek\film.mkv", card.Unit.Paths);
        Assert.Equal(@"E:\Yedek\film.mkv", vm.Session.Snapshot!.Units.Single(u => u.Id == Films.Id).Keep);
    }

    [AvaloniaFact]
    public async Task Karantina_Istegi_Kalacak_Kopyayi_Tasir_Kalici_Silme_Yok()
    {
        var backend = new FakeBackend();
        var vm = await Found(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.SelectedFilter = vm.Offers.Filters.First(f => f.Label == "Kopyalar");
        await Settle();
        var card = vm.Offers.Cards.First(c => c.Unit.Id == Films.Id);
        Assert.True(card.NeverPurge);
        Assert.False(card.CanPurge);

        await vm.Offers.PurgeOneCommand.ExecuteAsync(card);
        await vm.Offers.PurgeOneCommand.ExecuteAsync(card);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == Films.Id);

        card.Copies.First(c => c.Path == @"E:\Yedek\film.mkv").KeepCommand.Execute(null);
        await vm.Offers.RemoveOneCommand.ExecuteAsync(card);
        await Settle();

        var request = Assert.Single(backend.Requests, r => r.UnitId == Films.Id);
        Assert.Equal(Ops.Quarantine, request.Op);
        Assert.Equal(@"E:\Yedek\film.mkv", request.Target);
        Assert.DoesNotContain(@"E:\Yedek\film.mkv", request.Paths);
        Assert.Equal(2, request.Paths.Count);
        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == Films.Id);
    }

    [AvaloniaFact]
    public async Task Arama_Iptal_Edilebilir()
    {
        SessionState.DuplicateFlushDelay = TimeSpan.FromMilliseconds(1);
        var backend = new FakeBackend { HoldDuplicates = new TaskCompletionSource() };
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());

        vm.Session.FindDuplicates();
        await Settle();
        Assert.True(vm.Session.IsFindingDuplicates);
        Assert.True(vm.Session.CancelDuplicatesCommand.CanExecute(null));

        vm.Session.CancelDuplicatesCommand.Execute(null);
        await vm.Session.DuplicateSearch.WaitAsync(TimeSpan.FromSeconds(5));
        await Settle();

        Assert.False(vm.Session.IsFindingDuplicates);
        Assert.Equal("Kopya araması durduruldu", vm.Session.DuplicateStatus);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task Ekranda_Kalacak_Isareti_Ve_Bunu_Tut_Dugmesi_Gorunur()
    {
        var backend = new FakeBackend();
        var vm = await Found(backend);
        var saved = MainWindow.ContextFactory;
        MainWindow.ContextFactory = () => vm;
        try
        {
            var window = new MainWindow { Width = 1200, Height = 780 };
            window.Show();
            await Settle();
            vm.GoTo(vm.Offers);
            await vm.Offers.Ready;
            vm.Offers.SelectedFilter = vm.Offers.Filters.First(f => f.Label == "Kopyalar");
            await Settle();

            var lists = window.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Name == "CopyList" && p.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(lists);
            var texts = lists.SelectMany(l => l.GetVisualDescendants().OfType<TextBlock>()).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Kalacak", texts);
            Assert.Contains(@"D:\Arşiv\film.mkv", texts);
            var keepButtons = lists.SelectMany(l => l.GetVisualDescendants().OfType<Button>()).Where(b => b.IsEffectivelyVisible && Equals(b.Content, "Bunu tut")).ToList();
            Assert.Equal(3, keepButtons.Count);

            var status = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "DuplicateStatus");
            Assert.True(status.IsEffectivelyVisible);
            Assert.Contains("kopya grubu", status.Text);

            keepButtons.First(b => b.DataContext is CopyRow { Path: @"E:\Yedek\film.mkv" }).Command!.Execute(null);
            await Settle();
            Assert.Equal(@"E:\Yedek\film.mkv", vm.Offers.Cards.First(c => c.Unit.Id == Films.Id).Unit.Keep);
            window.Close();
        }
        finally
        {
            MainWindow.ContextFactory = saved;
        }
    }
}
