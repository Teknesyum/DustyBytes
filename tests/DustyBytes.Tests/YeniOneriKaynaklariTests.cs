using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Units;
using DustyBytes.Worker;

namespace DustyBytes.Tests;

public class YeniOneriKaynaklariTests
{
    static readonly Unit OldDownloads = new()
    {
        Id = "OldDownload-abc",
        Kind = UnitKind.OldDownload,
        Name = OldDownloadsExtractor.Title,
        Label = "Eski indirme",
        Paths = [@"C:\Users\alice\Downloads\foto.jpg"],
        SizeBytes = 2_000_000_000,
        ContainsUserData = true,
        Removal = RemovalMethod.Quarantine,
        Effect = OldDownloadsExtractor.Effect,
        Score = 6,
    };

    static MainViewModel Shell(FakeBackend backend)
    {
        var vm = new MainViewModel(backend);
        var s = FakeBackend.Snapshot();
        vm.Session.SetSnapshot(new ScanSnapshot(s.Result, [.. s.Units, OldDownloads], s.FinishedAt, s.Method));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Confirm) && vm.Confirm is { } dialog)
                Dispatcher.UIThread.Post(() => dialog.AcceptCommand.Execute(null));
        };
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

    [AvaloniaFact]
    public async Task Eski_Indirme_Karti_Kalici_Silinemez()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        var card = vm.Offers.Cards.First(c => c.Unit.Kind == UnitKind.OldDownload);
        Assert.True(card.NeverPurge);
        Assert.False(card.CanPurge);

        await vm.Offers.PurgeOneCommand.ExecuteAsync(card);
        await vm.Offers.PurgeOneCommand.ExecuteAsync(card);
        await Settle();
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == OldDownloads.Id);
    }

    [AvaloniaFact]
    public async Task Toplu_Kalici_Silmede_Eski_Indirme_Karantinaya_Gider()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.Cards.First(c => c.Unit.Id == "u1").IsSelected = true;
        vm.Offers.Cards.First(c => c.Unit.Kind == UnitKind.OldDownload).IsSelected = true;

        await vm.Offers.PurgeSelectedCommand.ExecuteAsync(null);
        await vm.Offers.PurgeSelectedCommand.ExecuteAsync(null);
        await Settle();

        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Delete && r.UnitId == OldDownloads.Id);
        Assert.Contains(backend.Requests, r => r.Op == Ops.Quarantine && r.UnitId == OldDownloads.Id);
        Assert.Contains(backend.Requests, r => r.Op == Ops.Delete && r.UnitId == "u1");
    }

    [Fact]
    public void Worker_Eski_Indirmeyi_Kalici_Silmeyi_Reddeder()
    {
        Assert.True(WorkerServices.QuarantineOnly(new WorkerRequest { Op = Ops.Delete, UnitId = OldDownloads.Id }));
        Assert.False(WorkerServices.QuarantineOnly(new WorkerRequest { Op = Ops.Delete, UnitId = "Game-abc" }));
        Assert.False(WorkerServices.QuarantineOnly(new WorkerRequest { Op = Ops.Delete }));
    }

    [Fact]
    public async Task Sessiz_Temizlik_Hazirda_Bekletmeyi_Ve_Yeni_Copu_Almaz()
    {
        var backend = FakeBackend.Rich();
        backend.Tasks =
        [
            .. backend.Tasks,
            new SystemTaskInfo("recycle-bin-old", "Geri Dönüşüm Kutusu · 30 günden eski", 2_000_000, true, "Eski öğeler", true, null),
            new SystemTaskInfo("recycle-bin-recent", "Geri Dönüşüm Kutusu · son 30 gün", 1_000_000, false, "Yeni öğeler", true, null, Silent: false),
            new SystemTaskInfo("hibernation", "Hazırda bekletme dosyası", 8_000_000_000, true, "hiberfil.sys", true, null, Silent: false, RestoreId: null),
        ];

        var (_, tasks) = await CleanupViewModel.SafeDefaultsAsync(backend, CancellationToken.None);

        Assert.Equal(["temp", "recycle-bin-old"], tasks);
    }
}
