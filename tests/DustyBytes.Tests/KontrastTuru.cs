using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using DustyBytes.App;
using DustyBytes.App.Kontrast;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Ipc;
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

        var ilkTarama = vm.Session.Snapshot!;
        vm.Session.SetSnapshot(ilkTarama with
        {
            Units =
            [
                .. ilkTarama.Units,
                .. Enumerable.Range(1, 30).Select(i => new Unit
                {
                    Id = "d" + i,
                    Kind = UnitKind.DevArtifact,
                    Name = "src-tauri (Cargo)",
                    Paths = [$@"C:\Kod\Proje {i}\src-tauri\target"],
                    SizeBytes = 100_000_000L * i,
                    Removal = RemovalMethod.DirectDelete,
                }),
            ],
        });
        Bekle(vm.Overview.EstimateAsync());
        var grup = vm.Overview.SafeItems.First(r => r.IsGroup);
        Bekle(vm.Overview.ExpandSafeCommand.ExecuteAsync(grup));
        vm.Overview.SkipUnitCommand.Execute(grup.Units[1]);
        Otur();
        yield return "Güvenli Döküm Grup";
        vm.Overview.SkipUnitCommand.Execute(grup.Units[1]);
        vm.Session.SetSnapshot(ilkTarama);
        Bekle(vm.Overview.EstimateAsync());

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

        var icerik = IcerikKlasoru();
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
                    Paths = [icerik],
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
        var turKarti = belirsiz.Tour.Current!;
        turKarti.Contents!.ToggleCommand.Execute(null);
        Bekle(turKarti.Contents.Ready);
        Otur();
        yield return "Tur İçindekiler";
        turKarti.Contents.ToggleCommand.Execute(null);
        belirsiz.Tour.EndCommand.Execute(null);
        belirsiz.Tour.CloseCommand.Execute(null);

        belirsiz.GoTo(belirsiz.Offers);
        Bekle(belirsiz.Offers.Ready);
        belirsiz.Offers.ShowFilter("Klasör");
        var oneriKarti = belirsiz.Offers.Cards.First(c => c.Unit.Id == "k1");
        oneriKarti.Contents!.ToggleCommand.Execute(null);
        Bekle(oneriKarti.Contents.Ready);
        Otur();
        yield return "Öneriler İçindekiler";
        var secim = oneriKarti.Contents;
        secim.Rows[0].IsSelected = true;
        secim.Rows[1].IsSelected = true;
        Otur();
        yield return "Öneriler İçindekiler Seçili";
        Bekle(secim.PurgeSelectedCommand.ExecuteAsync(null));
        Otur();
        yield return "Öneriler İçindekiler Kalıcı Sil";
        belirsiz.Offers.Purge.Reset();
        var kilitli = secim.Rows[1].Path;
        ((FakeBackend)belirsiz.Backend).Respond = r => new WorkerResponse
        {
            Id = r.Id,
            Ok = false,
            Items = [.. r.Paths.Select(p => p == kilitli
                ? new ItemResult(p, false, "Locked: dosya kullanımda — Tutan: oynatici (42)")
                : new ItemResult(p + "|k1-0", true, "", 1024))],
        };
        Bekle(secim.QuarantineSelectedCommand.ExecuteAsync(null));
        Bekle(belirsiz.Offers.Ready);
        Otur();
        yield return "Öneriler İçindekiler Sonuç";
        ((FakeBackend)belirsiz.Backend).Respond = null;
        secim.ToggleCommand.Execute(null);

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
        try
        {
            Directory.Delete(icerik, true);
        }
        catch (IOException)
        {
        }
    }

    static string IcerikKlasoru()
    {
        var kok = Path.Combine(Path.GetTempPath(), "dustybytes-kontrast-" + Environment.ProcessId);
        if (Directory.Exists(kok))
            Directory.Delete(kok, true);
        Directory.CreateDirectory(Path.Combine(kok, "Ekstra"));
        File.WriteAllBytes(Path.Combine(kok, "Tatil 2019.mkv"), new byte[64 * 1024]);
        File.WriteAllBytes(Path.Combine(kok, "Ekstra", "Kamera.mp3"), new byte[16 * 1024]);
        File.WriteAllBytes(Path.Combine(kok, "notlar.txt"), new byte[2 * 1024]);
        File.WriteAllBytes(Path.Combine(kok, "kapak.bmp"), Bmp());
        return kok;
    }

    static byte[] Bmp()
    {
        var veri = new byte[70];
        veri[0] = (byte)'B';
        veri[1] = (byte)'M';
        BitConverter.GetBytes(70).CopyTo(veri, 2);
        BitConverter.GetBytes(54).CopyTo(veri, 10);
        BitConverter.GetBytes(40).CopyTo(veri, 14);
        BitConverter.GetBytes(2).CopyTo(veri, 18);
        BitConverter.GetBytes(2).CopyTo(veri, 22);
        BitConverter.GetBytes((short)1).CopyTo(veri, 26);
        BitConverter.GetBytes((short)24).CopyTo(veri, 28);
        BitConverter.GetBytes(16).CopyTo(veri, 34);
        for (var i = 54; i < 70; i++)
            veri[i] = 0x80;
        return veri;
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
