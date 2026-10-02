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
        Otur();
        yield return "Yükleniyor";
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
