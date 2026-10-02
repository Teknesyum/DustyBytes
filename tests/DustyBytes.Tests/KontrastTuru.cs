using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using DustyBytes.App;
using DustyBytes.App.Kontrast;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

static class KontrastTuru
{
    [ModuleInitializer]
    public static void Init() => KontrastEkranlari.Gez = Tur;

    static IEnumerable<string> Tur(Window pencere)
    {
        var vm = (MainViewModel)pencere.DataContext!;
        foreach (var item in vm.NavItems)
        {
            vm.SelectedNav = item;
            Otur();
            yield return item.Label;
        }
        vm.SelectedNav = vm.NavItems.First(n => n.Screen == vm.Programs);
        vm.Programs.Selected = vm.Programs.Rows.FirstOrDefault(r => r.CanUninstall);
        if (vm.Programs.UninstallCommand.CanExecute(null))
        {
            vm.Programs.UninstallCommand.Execute(null);
            Otur();
            yield return "Kaldırma";
        }

        vm.SelectedNav = vm.NavItems[0];
        vm.Notify("Tarama bitti: 42 birim, 18,4 GB açılabilir", "Önerilere git", () => Task.CompletedTask).IsPaused = true;
        vm.Fail("Karantinaya taşınamadı: dosya kullanımda");
        Otur();
        yield return "Bildirimler";
        vm.Toasts.Clear();

        vm.Report = new SessionReportViewModel(vm, new SessionReport
        {
            Id = "rapor",
            Title = "Tur",
            FreeBefore = 48_000_000_000,
            FreeAfter = 62_000_000_000,
            QuarantinedBytes = 80_000_000_000,
            QuarantinedCount = 23,
            PurgedBytes = 3_000_000_000,
            PurgedCount = 1,
        });
        Otur();
        yield return "Oturum Raporu";
        vm.Report = null;

        _ = vm.ConfirmAsync("Karantinaya taşınsın mı?", "Seçilen 3 birim karantinaya taşınır; 7 gün içinde geri alınabilir.", "Taşı");
        Otur();
        yield return "Onay Tehlikeli";
        vm.Confirm = null;
        _ = vm.ConfirmAsync("Güncelleme yüklensin mi?", "Program kapanıp yeni sürümle yeniden açılır.", "Yükle", false);
        Otur();
        yield return "Onay Olağan";
        vm.Confirm = null;

        vm.Update.State = UpdateState.Available;
        vm.Update.IsPanelOpen = true;
        Otur();
        yield return "Güncelleme Var";
        vm.Update.Percent = 42;
        vm.Update.State = UpdateState.Downloading;
        Otur();
        yield return "Güncelleme İniyor";
        vm.Update.State = UpdateState.Ready;
        Otur();
        yield return "Güncelleme Hazır";
        vm.Update.IsPanelOpen = false;

        vm.GoTo(vm.Overview);
        Bekle(vm.Overview.EstimateAsync());
        vm.Overview.Target.GoalText = "60";
        vm.Overview.Target.EditCommand.Execute(null);
        Otur();
        yield return "Hedef Planı";
        vm.Overview.Target.FreeNowCommand.Execute(null);
        Otur();
        yield return "Hedef Şimdi Yer Aç";
        vm.Overview.Target.Disarm();
        vm.Overview.Target.GoalText = "";

        var acik = vm.Overview.SafeItems.First(r => r.CanExpand);
        var atla = vm.Overview.SafeItems.First(r => r != acik);
        Bekle(vm.Overview.ExpandSafeCommand.ExecuteAsync(acik));
        vm.Overview.SkipSafeCommand.Execute(atla);
        Otur();
        yield return "Güvenli Döküm";
        vm.Overview.SkipSafeCommand.Execute(atla);
        Bekle(vm.Overview.ExpandSafeCommand.ExecuteAsync(acik));

        Bekle(vm.StartTourAsync(TourMode.Ask));
        Otur();
        yield return "Tur Kümesi";
        vm.Tour.Key(TourKey.Right);
        Otur();
        yield return "Tur Kümesi Kullanıcı Verisi";
        vm.Tour.SelectCommand.Execute(null);
        Otur();
        yield return "Tur Kümesi Seçerek";
        vm.Tour.EndCommand.Execute(null);
        vm.Tour.CloseCommand.Execute(null);

        var snapshot = FakeBackend.Snapshot();
        var belirsiz = new MainViewModel(FakeBackend.Rich());
        belirsiz.Session.SetSnapshot(snapshot with
        {
            Units =
            [
                .. snapshot.Units,
                new Unit
                {
                    Id = "k1",
                    Kind = UnitKind.Folder,
                    Name = "Eski Klasör",
                    Paths = [@"C:\Eski Klasör"],
                    SizeBytes = 30_000_000_000,
                    Usage = new UsageSignal(DateTimeOffset.Now.AddDays(-200), "prefetch", 0.9),
                    Confidence = 0.4,
                },
            ],
        });
        pencere.DataContext = belirsiz;
        belirsiz.GoTo(belirsiz.Overview);
        Bekle(belirsiz.StartTourAsync(TourMode.Ask));
        belirsiz.Tour.KeepCommand.Execute(null);
        belirsiz.Tour.KeepCommand.Execute(null);
        Otur();
        yield return "Tur Emin Değiliz";
        belirsiz.Tour.PurgeOneCommand.Execute(null);
        Otur();
        yield return "Tur Emin Değiliz Kalıcı Sil";
        belirsiz.Tour.Purge.Reset();

        var bos = new MainViewModel(new FakeBackend { DryRun = true });
        pencere.DataContext = bos;
        foreach (var item in bos.NavItems)
        {
            bos.SelectedNav = item;
            Otur();
            yield return "Boş " + item.Label;
        }

        var hata = new MainViewModel(new FakeBackend());
        pencere.DataContext = hata;
        hata.Session.ScanError = "Yönetici işçisi başlatılamadı: erişim reddedildi";
        hata.Fail("Tarama tamamlanamadı: Yönetici işçisi başlatılamadı");
        Otur();
        yield return "Hata";

        var yukleniyor = new MainViewModel(new FakeBackend());
        pencere.DataContext = yukleniyor;
        var bekle = new TaskCompletionSource<ScanSnapshot>();
        _ = yukleniyor.Session.Scan.RunAsync("C: sürücüsü taranıyor", (p, _) =>
        {
            p.Report(new TaskStep("Dosyalar sayılıyor", 42, @"C:\Games\Steam\steamapps"));
            return bekle.Task;
        });
        yukleniyor.Session.Scan.AddLine("Kullanım izleri okunuyor", true);
        yukleniyor.Session.Scan.SetCounters(FakeBackend.Snapshot().Units);
        Otur();
        yield return "Yükleniyor";
    }

    static void Bekle(Task gorev)
    {
        for (var i = 0; i < 400 && !gorev.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    static void Otur()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
