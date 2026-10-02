using System.Diagnostics;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public sealed class BirimIcerigiTests : IDisposable
{
    readonly string _kok = Path.Combine(Path.GetTempPath(), "db-icerik-" + Guid.NewGuid().ToString("N"));

    public BirimIcerigiTests() => Directory.CreateDirectory(_kok);

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

    [Theory]
    [InlineData("film.mp4")]
    [InlineData("Film.MKV")]
    [InlineData("sarki.flac")]
    [InlineData("foto.jpeg")]
    [InlineData("tarama.heic")]
    public void Izinli_Medya_Acilabilir(string ad)
    {
        var yol = Yaz(ad, 10);
        Assert.Null(SafeOpen.Refuse(yol));
    }

    [Theory]
    [InlineData("kur.exe")]
    [InlineData("betik.bat")]
    [InlineData("betik.cmd")]
    [InlineData("betik.ps1")]
    [InlineData("kisayol.lnk")]
    [InlineData("baglanti.url")]
    [InlineData("ekran.scr")]
    [InlineData("paket.msi")]
    [InlineData("betik.js")]
    [InlineData("betik.vbs")]
    [InlineData("uygulama.hta")]
    [InlineData("film.mp4.exe")]
    [InlineData("film.mp4.lnk")]
    [InlineData("notlar.txt")]
    [InlineData("belge.pdf")]
    [InlineData("uzantisiz")]
    public void Izinsiz_Tur_Reddedilir(string ad)
    {
        var yol = Yaz(ad, 10);
        Assert.NotNull(SafeOpen.Refuse(yol));
        Assert.NotNull(SafeOpen.Open(yol));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("film.mp4")]
    [InlineData(@"klasor\film.mp4")]
    [InlineData(@"C:film.mp4")]
    [InlineData(@"\film.mp4")]
    [InlineData(@"\\sunucu\paylasim\film.mp4")]
    [InlineData(@"\\?\C:\film.mp4")]
    [InlineData(@"\\.\C:\film.mp4")]
    [InlineData("//sunucu/paylasim/film.mp4")]
    [InlineData(@"C:\klasor\kur.exe:akis.mp4")]
    [InlineData(@"C:\klasor\film.mp4.")]
    [InlineData(@"C:\klasor\film.mp4 ")]
    [InlineData("C:\\klasor\\\"film.mp4")]
    public void Gecersiz_Yol_Reddedilir(string? yol)
    {
        Assert.NotNull(SafeOpen.Refuse(yol));
        Assert.NotNull(SafeOpen.RefuseReveal(yol));
    }

    [Fact]
    public void Olmayan_Dosya_Reddedilir()
    {
        Assert.NotNull(SafeOpen.Refuse(Path.Combine(_kok, "yok.mp4")));
        Assert.NotNull(SafeOpen.RefuseReveal(Path.Combine(_kok, "yok.mp4")));
    }

    [Fact]
    public void Klasorde_Goster_Her_Dosyada_Var()
    {
        var yol = Yaz("notlar.txt", 10);
        Assert.Null(SafeOpen.RefuseReveal(yol));
        Assert.Null(SafeOpen.RefuseReveal(_kok));
    }

    [Theory]
    [InlineData("a.mkv", FileKind.Video)]
    [InlineData("a.M2TS", FileKind.Video)]
    [InlineData("a.opus", FileKind.Audio)]
    [InlineData("a.wma", FileKind.Audio)]
    [InlineData("a.webp", FileKind.Image)]
    [InlineData("a.TIFF", FileKind.Image)]
    [InlineData("a.exe", FileKind.Other)]
    [InlineData("a.mp4.exe", FileKind.Other)]
    [InlineData("a", FileKind.Other)]
    public void Tur_Siniflanir(string ad, FileKind beklenen) => Assert.Equal(beklenen, SafeOpen.KindOf(ad));

    [Fact]
    public void En_Buyuk_N_Buyukten_Kucuge_Gelir()
    {
        Yaz("a.mp4", 500);
        Yaz(@"alt\b.jpg", 900);
        Yaz(@"alt\derin\c.mp3", 700);
        Yaz("d.txt", 100);
        Yaz(@"alt\e.exe", 300);

        var sonuc = UnitContents.List([_kok], 3, UnitContents.MaxDepth, TimeSpan.FromSeconds(30));

        Assert.Equal(["b.jpg", "c.mp3", "a.mp4"], sonuc.Files.Select(f => f.Name));
        Assert.Equal([FileKind.Image, FileKind.Audio, FileKind.Video], sonuc.Files.Select(f => f.Kind));
        Assert.Equal(5, sonuc.FileCount);
        Assert.Equal(2500, sonuc.TotalBytes);
        Assert.False(sonuc.Partial);
    }

    [Fact]
    public void Derinlik_Siniri_Asilmaz()
    {
        Yaz(@"1\ust.mp4", 10);
        Yaz(@"1\2\3\derin.mp4", 1000);

        var sonuc = UnitContents.List([_kok], 30, 2, TimeSpan.FromSeconds(30));

        Assert.Equal(["ust.mp4"], sonuc.Files.Select(f => f.Name));
        Assert.True(sonuc.Partial);
    }

    [Fact]
    public void Baglanti_Noktasi_Izlenmez()
    {
        var dis = Path.Combine(_kok, "dis");
        Yaz(@"dis\buyuk.mkv", 5000);
        var ic = Path.Combine(_kok, "ic");
        Yaz(@"ic\kucuk.mp4", 10);
        var baglanti = Path.Combine(ic, "kopru");
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{baglanti}\" \"{dis}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            p!.WaitForExit();
        if (!Directory.Exists(baglanti))
            return;

        var sonuc = UnitContents.List([ic], 30, UnitContents.MaxDepth, TimeSpan.FromSeconds(30));

        Assert.Equal(["kucuk.mp4"], sonuc.Files.Select(f => f.Name));
    }

    [Fact]
    public void Olmayan_Ve_Bos_Yol_Sessizce_Atlanir()
    {
        var sonuc = UnitContents.List([Path.Combine(_kok, "yok"), "", _kok], 30, 4, TimeSpan.FromSeconds(30));
        Assert.Empty(sonuc.Files);
        Assert.Equal(0, sonuc.FileCount);
    }

    [Fact]
    public void Film_Oynat_En_Buyuk_Videoyu_Hedefler()
    {
        Yaz(@"Film\kapak.png", 5000);
        var ana = Yaz(@"Film\Film.2019.mkv", 3000);
        Yaz(@"Film\Ekstra\fragman.mp4", 1000);
        Yaz(@"Film\altyazi.srt", 50);

        var sonuc = UnitContents.List([Path.Combine(_kok, "Film")]);

        Assert.Equal("kapak.png", sonuc.Files[0].Name);
        Assert.Equal(ana, UnitContents.MainVideo(sonuc.Files));
        Assert.Null(SafeOpen.Refuse(UnitContents.MainVideo(sonuc.Files)));
    }

    [Fact]
    public void Videosuz_Klasorde_Oynat_Hedefi_Yok()
    {
        Yaz(@"Klasor\resim.jpg", 100);
        Assert.Null(UnitContents.MainVideo(UnitContents.List([Path.Combine(_kok, "Klasor")]).Files));
    }

    static Unit Birim(UnitKind tur, string yol) => new()
    {
        Id = "u",
        Kind = tur,
        Name = "Birim",
        Paths = [yol],
        SizeBytes = 1,
    };

    [Fact]
    public async Task Kart_Icindekileri_Yukler_Ve_Filmde_Oynatir()
    {
        Yaz(@"Film\film.mkv", 3000);
        Yaz(@"Film\kapak.jpg", 200);
        Yaz(@"Film\notlar.nfo", 20);
        var kart = new UnitCard(Birim(UnitKind.Film, Path.Combine(_kok, "Film")), DateTimeOffset.Now, () => { });

        Assert.True(kart.HasContents);
        Assert.True(kart.CanPlay);
        Assert.False(kart.Contents!.IsExpanded);
        kart.Contents.ToggleCommand.Execute(null);
        Assert.True(kart.Contents.IsExpanded);
        await kart.Contents.Ready;

        Assert.Equal(["film.mkv", "kapak.jpg", "notlar.nfo"], kart.Contents.Rows.Select(r => r.Name));
        Assert.Equal([true, true, false], kart.Contents.Rows.Select(r => r.CanOpen));
        Assert.Equal("Oynat", kart.Contents.Rows[0].OpenText);
        Assert.Equal("Aç", kart.Contents.Rows[1].OpenText);
        Assert.Contains("3 dosya", kart.Contents.Summary);
    }

    [Theory]
    [InlineData(UnitKind.Film, true)]
    [InlineData(UnitKind.Series, true)]
    [InlineData(UnitKind.Folder, true)]
    [InlineData(UnitKind.Game, true)]
    [InlineData(UnitKind.Program, true)]
    [InlineData(UnitKind.Cache, false)]
    [InlineData(UnitKind.Duplicate, false)]
    public void Icindekiler_Yalniz_Icerikli_Turlerde(UnitKind tur, bool beklenen)
    {
        var kart = new UnitCard(Birim(tur, _kok), DateTimeOffset.Now, () => { });
        Assert.Equal(beklenen, kart.HasContents);
        Assert.Equal(tur == UnitKind.Film, kart.CanPlay);
    }
}
