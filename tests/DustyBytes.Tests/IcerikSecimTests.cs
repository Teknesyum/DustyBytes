using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Worker;

namespace DustyBytes.Tests;

public sealed class IcerikSecimTests : IDisposable
{
    readonly string _kok = Path.Combine(Path.GetTempPath(), "db-secim-" + Guid.NewGuid().ToString("N"));

    public IcerikSecimTests() => Directory.CreateDirectory(_kok);

    public void Dispose()
    {
        try
        {
            foreach (var link in Directory.EnumerateDirectories(_kok, "*", SearchOption.AllDirectories)
                         .Where(d => new DirectoryInfo(d).LinkTarget is not null).ToList())
                Directory.Delete(link);
            Directory.Delete(_kok, true);
        }
        catch (IOException)
        {
        }
    }

    string Yaz(string goreli, int bayt)
    {
        var yol = Path.Combine(_kok, goreli);
        Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
        File.WriteAllBytes(yol, new byte[bayt]);
        return yol;
    }

    static Unit Birim(string yol, long boyut = 10_000, RemovalMethod kaldirma = RemovalMethod.Quarantine) => new()
    {
        Id = "f1",
        Kind = UnitKind.Folder,
        Name = "Birim",
        Paths = [yol],
        SizeBytes = boyut,
        Removal = kaldirma,
    };

    async Task<UnitContentsViewModel> Icerik(FakeBackend arka, Action<ContentsRemoval>? bildir = null, RemovalMethod kaldirma = RemovalMethod.Quarantine)
    {
        var icerik = new UnitContentsViewModel(Birim(Path.Combine(_kok, "Birim"), kaldirma: kaldirma));
        icerik.Connect(arka, new TwoStep(), bildir ?? (_ => { }));
        await icerik.EnsureAsync();
        return icerik;
    }

    [Fact]
    public async Task Tum_Dosyalar_Sayfa_Sayfa_Gelir()
    {
        for (var i = 0; i < 120; i++)
            Yaz($@"Birim\alt{i % 3}\d{i:D3}.bin", 1000 + i);

        var icerik = await Icerik(new FakeBackend());

        Assert.Equal(UnitContentsViewModel.PageSize, icerik.Rows.Count);
        Assert.Equal("d119.bin", icerik.Rows[0].Name);
        Assert.True(icerik.HasMore);
        Assert.Contains("70 dosya kaldı", icerik.MoreText);
        icerik.ShowMoreCommand.Execute(null);
        icerik.ShowMoreCommand.Execute(null);
        Assert.Equal(120, icerik.Rows.Count);
        Assert.False(icerik.HasMore);
        Assert.Equal("d000.bin", icerik.Rows[^1].Name);
        Assert.Contains("120 dosya", icerik.Summary);
    }

    [Fact]
    public void Sure_Asilinca_Ilk_N_Dosya_Denir()
    {
        Yaz(@"Birim\a.bin", 10);
        var sonuc = UnitContents.List([Path.Combine(_kok, "Birim")], 10, UnitContents.MaxDepth, TimeSpan.Zero);
        Assert.True(sonuc.Partial);
    }

    [Fact]
    public async Task Secim_Sayisi_Ve_Boyutu_Toplanir()
    {
        Yaz(@"Birim\buyuk.mkv", 3000);
        Yaz(@"Birim\orta.mp3", 2000);
        Yaz(@"Birim\kucuk.txt", 1000);
        var icerik = await Icerik(new FakeBackend());

        Assert.True(icerik.CanSelect);
        Assert.True(icerik.ShowSelection);
        Assert.False(icerik.QuarantineSelectedCommand.CanExecute(null));
        Assert.Equal("Hiçbir dosya seçilmedi", icerik.SelectionText);

        icerik.Rows[0].IsSelected = true;
        icerik.Rows[2].IsSelected = true;
        Assert.Equal(2, icerik.SelectedCount);
        Assert.Equal(4000, icerik.SelectedBytes);
        Assert.StartsWith("Seçilenleri karantinaya al (2 dosya", icerik.QuarantineText);
        Assert.True(icerik.QuarantineSelectedCommand.CanExecute(null));

        icerik.SelectAllCommand.Execute(null);
        Assert.Equal(3, icerik.SelectedCount);
        Assert.Equal(6000, icerik.SelectedBytes);
        icerik.SelectNoneCommand.Execute(null);
        Assert.Equal(0, icerik.SelectedCount);
        Assert.Equal(0, icerik.SelectedBytes);
    }

    [Fact]
    public async Task Oynat_Ve_Ac_Yalniz_Izinli_Turlerde()
    {
        Yaz(@"Birim\film.mkv", 3000);
        Yaz(@"Birim\sarki.mp3", 2000);
        Yaz(@"Birim\kur.exe", 1000);
        var icerik = await Icerik(new FakeBackend());

        Assert.Equal([true, true, false], icerik.Rows.Select(r => r.CanOpen));
        Assert.Equal(["Oynat", "Oynat"], icerik.Rows.Take(2).Select(r => r.OpenText));
        Assert.NotNull(SafeOpen.Refuse(icerik.Rows[2].Path));
    }

    [Fact]
    public async Task Baglanti_Yoksa_Ya_Da_Birim_Karantinalik_Degilse_Secim_Yok()
    {
        Yaz(@"Birim\a.mkv", 10);
        var yalin = new UnitContentsViewModel(Birim(Path.Combine(_kok, "Birim")));
        await yalin.EnsureAsync();
        Assert.False(yalin.CanSelect);
        Assert.False(yalin.Rows[0].CanSelect);

        var baslatici = await Icerik(new FakeBackend(), kaldirma: RemovalMethod.Launcher);
        Assert.False(baslatici.CanSelect);
    }

    [Fact]
    public async Task Varsayilan_Karantina_Istegi_Kok_Ve_Onayla_Gider()
    {
        var film = Yaz(@"Birim\film.mkv", 3000);
        Yaz(@"Birim\kapak.jpg", 200);
        var arka = new FakeBackend();
        ContentsRemoval? bildirilen = null;
        var icerik = await Icerik(arka, r => bildirilen = r);

        icerik.Rows[0].IsSelected = true;
        await icerik.QuarantineSelectedCommand.ExecuteAsync(null);

        var istek = Assert.Single(arka.Requests);
        Assert.Equal(Ops.Quarantine, istek.Op);
        Assert.Equal([film], istek.Paths);
        Assert.Equal([Path.Combine(_kok, "Birim")], istek.Roots);
        Assert.Equal("f1", istek.UnitId);
        Assert.True(istek.UserApproved);
        Assert.Equal(["kapak.jpg"], icerik.Rows.Select(r => r.Name));
        Assert.Equal(200, icerik.TotalBytes);
        Assert.Equal(1, icerik.FileCount);
        Assert.Contains("1 dosya", icerik.Summary);
        Assert.Contains("karantinada", icerik.Notice);
        Assert.Equal(0, icerik.SelectedCount);
        Assert.NotNull(bildirilen);
        Assert.Equal(3000, bildirilen!.Bytes);
        Assert.False(bildirilen.Purged);
        Assert.False(bildirilen.Empty);
    }

    [Fact]
    public async Task Kalici_Silme_Iki_Basis_Ister()
    {
        Yaz(@"Birim\film.mkv", 3000);
        var arka = new FakeBackend();
        var icerik = await Icerik(arka);
        icerik.Rows[0].IsSelected = true;

        await icerik.PurgeSelectedCommand.ExecuteAsync(null);
        Assert.Empty(arka.Requests);
        Assert.True(icerik.IsPurgeArmed);
        Assert.Equal(TwoStep.ArmedText, icerik.PurgeText);

        icerik.Rows[0].IsSelected = false;
        Assert.False(icerik.IsPurgeArmed);
        icerik.Rows[0].IsSelected = true;

        await icerik.PurgeSelectedCommand.ExecuteAsync(null);
        await icerik.PurgeSelectedCommand.ExecuteAsync(null);
        var istek = Assert.Single(arka.Requests);
        Assert.Equal(Ops.Delete, istek.Op);
        Assert.False(icerik.IsPurgeArmed);
        Assert.Empty(icerik.Rows);
    }

    [Fact]
    public async Task Basarisizlar_Sebebiyle_Listede_Kalir()
    {
        var film = Yaz(@"Birim\film.mkv", 3000);
        var kilitli = Yaz(@"Birim\kilitli.mp4", 2000);
        var arka = new FakeBackend
        {
            Respond = r => new WorkerResponse
            {
                Id = r.Id,
                Ok = false,
                Items =
                [
                    new ItemResult(film + "|f1-0", true, "", 3000),
                    new ItemResult(kilitli, false, "Locked: dosya kullanımda — Tutan: oynatici (42)"),
                ],
            },
        };
        var icerik = await Icerik(arka);
        icerik.SelectAllCommand.Execute(null);

        await icerik.QuarantineSelectedCommand.ExecuteAsync(null);

        var kalan = Assert.Single(icerik.Rows);
        Assert.Equal("kilitli.mp4", kalan.Name);
        Assert.True(kalan.HasFailure);
        Assert.StartsWith("dosya kullanımda", kalan.Failure);
        Assert.Contains("1 dosya işlenemedi", icerik.Status);
        Assert.Equal(2000, icerik.TotalBytes);
    }

    [Fact]
    public async Task Prova_Listeyi_Degistirmez()
    {
        Yaz(@"Birim\film.mkv", 3000);
        var icerik = await Icerik(new FakeBackend { DryRun = true });
        icerik.Rows[0].IsSelected = true;

        await icerik.QuarantineSelectedCommand.ExecuteAsync(null);

        Assert.Single(icerik.Rows);
        Assert.StartsWith("Prova", icerik.Notice);
    }

    [Theory]
    [InlineData("Denied: Kullanıcının koruma istisnası", "Kullanıcının koruma istisnası")]
    [InlineData("Locked", "Dosya kullanımda")]
    [InlineData("NotFound: Yol bulunamadı", "Yol bulunamadı")]
    [InlineData("Bilinmeyen bir hata", "Bilinmeyen bir hata")]
    public void Sebep_Durum_Onekinden_Ayrilir(string mesaj, string beklenen) =>
        Assert.Equal(beklenen, UnitContentsViewModel.Reason(mesaj));

    [AvaloniaFact]
    public async Task Oneriler_Birimi_Kuculur_Ve_Icerik_Acik_Kalir()
    {
        Yaz(@"Birim\film.mkv", 3000);
        Yaz(@"Birim\kapak.jpg", 200);
        var arka = new FakeBackend();
        var vm = new MainViewModel(arka);
        var anlik = FakeBackend.Snapshot();
        vm.Session.SetSnapshot(anlik with { Units = [.. anlik.Units, Birim(Path.Combine(_kok, "Birim"), 50_000)] });
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.ShowSmall = true;
        var kart = vm.Offers.Cards.First(c => c.Unit.Id == "f1");
        var icerik = kart.Contents!;
        Assert.True(icerik.CanSelect);
        icerik.ToggleCommand.Execute(null);
        await icerik.Ready;

        icerik.Rows[0].IsSelected = true;
        await icerik.QuarantineSelectedCommand.ExecuteAsync(null);
        await Otur();
        await vm.Offers.Ready;

        Assert.Equal(47_000, vm.Session.Snapshot!.Units.Single(u => u.Id == "f1").SizeBytes);
        var yeni = vm.Offers.Cards.First(c => c.Unit.Id == "f1");
        Assert.Equal(47_000, yeni.Unit.SizeBytes);
        Assert.Same(icerik, yeni.Contents);
        Assert.True(yeni.Contents!.IsExpanded);
        Assert.Equal(["kapak.jpg"], yeni.Contents.Rows.Select(r => r.Name));

        yeni.Contents.Rows[0].IsSelected = true;
        await yeni.Contents.QuarantineSelectedCommand.ExecuteAsync(null);
        await Otur();
        await vm.Offers.Ready;
        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == "f1");
    }

    static async Task Otur()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    sealed class Isci
    {
        public Isci(string kok, Action<SafetyGate>? hazirla = null)
        {
            Gate = SafetyGate.LoadDefault();
            hazirla?.Invoke(Gate);
            Sunucu = new WorkerServer(WorkerClient.NewPipeName(), Environment.ProcessId, new WorkerServices(Gate, new QuarantineOptions
            {
                RootResolver = _ => Path.Combine(kok, ".dustybytes", "quarantine"),
                FallbackToRecycleBin = false,
            }), watchParent: false);
        }

        public SafetyGate Gate { get; }
        public WorkerServer Sunucu { get; }

        public Task<WorkerResponse> Gonder(string op, string kok, params string[] yollar) =>
            Sunucu.HandleAsync(new WorkerRequest
            {
                Op = op,
                Paths = [.. yollar],
                Roots = [kok],
                UnitId = "f1",
                UserApproved = true,
                IncludeUserData = true,
            }, new Progress<WorkerProgress>(), CancellationToken.None);
    }

    [Fact]
    public async Task Isci_Birim_Disindaki_Yolu_Reddeder()
    {
        var birim = Path.Combine(_kok, "Birim");
        Yaz(@"Birim\ic.mkv", 10);
        var dis = Yaz(@"Dis\dis.mkv", 10);
        var kardes = Yaz(@"Birim2\kardes.mkv", 10);
        var isci = new Isci(_kok);

        foreach (var op in new[] { Ops.Quarantine, Ops.Delete })
        {
            var yanit = await isci.Gonder(op, birim, dis, Path.Combine(birim, "..", "Dis", "dis.mkv"), kardes);
            Assert.False(yanit.Ok);
            Assert.All(yanit.Items, i => Assert.StartsWith("Denied: Yol birimin dışında", i.Message));
        }
        Assert.True(File.Exists(dis));
        Assert.True(File.Exists(kardes));
    }

    [Fact]
    public async Task Isci_Kok_Klasoru_Ve_Alt_Klasoru_Reddeder()
    {
        var birim = Path.Combine(_kok, "Birim");
        Yaz(@"Birim\alt\a.mkv", 10);
        var isci = new Isci(_kok);

        var yanit = await isci.Gonder(Ops.Quarantine, birim, birim, Path.Combine(birim, "alt"));

        Assert.False(yanit.Ok);
        Assert.All(yanit.Items, i => Assert.StartsWith("Denied", i.Message));
        Assert.True(File.Exists(Path.Combine(birim, "alt", "a.mkv")));
    }

    [Fact]
    public async Task Isci_Surucu_Kokunu_Birim_Koku_Saymaz()
    {
        var dosya = Yaz(@"Birim\a.mkv", 10);
        var isci = new Isci(_kok);

        var yanit = await isci.Gonder(Ops.Quarantine, Path.GetPathRoot(_kok)!, dosya);

        Assert.False(yanit.Ok);
        Assert.True(File.Exists(dosya));
    }

    [Fact]
    public async Task Isci_Baglanti_Noktasindan_Kacan_Yolu_Reddeder()
    {
        var birim = Path.Combine(_kok, "Birim");
        var dis = Yaz(@"Dis\gizli.mkv", 10);
        Directory.CreateDirectory(birim);
        var kopru = Path.Combine(birim, "kopru");
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{kopru}\" \"{Path.GetDirectoryName(dis)}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            p!.WaitForExit();
        if (!Directory.Exists(kopru))
            return;
        var isci = new Isci(_kok);

        var yanit = await isci.Gonder(Ops.Quarantine, birim, Path.Combine(kopru, "gizli.mkv"));

        Assert.False(yanit.Ok);
        Assert.Contains("bağlantı noktası", Assert.Single(yanit.Items).Message);
        Assert.True(File.Exists(dis));
    }

    [Fact]
    public async Task Isci_Korumali_Yolu_Birim_Icinde_De_Reddeder()
    {
        var birim = Path.Combine(_kok, "Birim");
        var korunan = Yaz(@"Birim\korunan.mkv", 10);
        var isci = new Isci(_kok, g => g.List.AddUserException(korunan));

        var yanit = await isci.Gonder(Ops.Quarantine, birim, korunan);

        Assert.False(yanit.Ok);
        Assert.StartsWith("Denied", Assert.Single(yanit.Items).Message);
        Assert.True(File.Exists(korunan));
    }

    [Fact]
    public async Task Isci_Birim_Icindeki_Dosyayi_Karantinaya_Alir()
    {
        if (DustyBytes.Core.DryRun.Enabled)
            return;
        var birim = Path.Combine(_kok, "Birim");
        var dosya = Yaz(@"Birim\alt\film.mkv", 1234);
        var kalan = Yaz(@"Birim\kalan.mkv", 10);
        var isci = new Isci(_kok);

        var yanit = await isci.Gonder(Ops.Quarantine, birim, dosya);

        Assert.True(yanit.Ok, yanit.Message);
        Assert.False(File.Exists(dosya));
        Assert.True(File.Exists(kalan));
        Assert.Equal(1234, yanit.PendingBytes);
        Assert.Contains("|", Assert.Single(yanit.Items).Path);
    }

    [Fact]
    public void Kapsam_Kok_Yoksa_Eski_Davranis_Korunur()
    {
        Assert.Null(UnitScope.Refuse(Path.Combine(_kok, "Birim", "a.mkv"), [Path.Combine(_kok, "Birim")]));
        Assert.NotNull(UnitScope.Refuse(Path.Combine(_kok, "Birim", "a.mkv:akis"), [Path.Combine(_kok, "Birim")]));
        Assert.NotNull(UnitScope.Refuse(@"\\sunucu\pay\a.mkv", [@"\\sunucu\pay"]));
        Assert.NotNull(UnitScope.Refuse("a.mkv", [Path.Combine(_kok, "Birim")]));
    }
}
