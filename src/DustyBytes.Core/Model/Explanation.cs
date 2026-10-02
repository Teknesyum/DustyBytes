namespace DustyBytes.Core.Model;

public sealed record Explanation(string What, string IfDeleted, string Returns)
{
    public const string WhatTitle = "Bu nedir?";
    public const string IfDeletedTitle = "Silersem ne olur?";
    public const string ReturnsTitle = "Geri gelir mi?";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(What) && !string.IsNullOrWhiteSpace(IfDeleted) && !string.IsNullOrWhiteSpace(Returns);

    public string Lines => $"{WhatTitle} {What}\n{IfDeletedTitle} {IfDeleted}\n{ReturnsTitle} {Returns}";
}

public static class UnitKindInfo
{
    public static Explanation Explain(UnitKind kind) => kind switch
    {
        UnitKind.Game => new(
            "Bilgisayarda kurulu bir oyun.",
            "Oyun kaldırılır; kayıtların çoğu oyunun bulutunda durur.",
            "Evet, mağazadan yeniden indirirsin; bulutta olmayan yerel kayıtlar gelmeyebilir."),
        UnitKind.Program => new(
            "Bilgisayarda kurulu bir program.",
            "Program kendi kaldırıcısıyla kaldırılır; belgelerin etkilenmez.",
            "Evet, sitesinden yeniden kurarsın; ayarları geri gelmeyebilir."),
        UnitKind.AppContent => new(
            "Bir uygulamanın indirdiği büyük içerik, örneğin yapay zekâ modeli ya da harita.",
            "Uygulama bu içeriği kaybeder; uygulamanın kendisi çalışmaya devam eder.",
            "Evet, uygulama içeriği gerektiğinde yeniden indirir."),
        UnitKind.Film => new(
            "Bilgisayarındaki bir film dosyası.",
            "Dosya bilgisayardan gider; başka yerde kopyan yoksa bir daha izleyemezsin.",
            "Hayır, ama karantinadan süresi içinde geri alabilirsin."),
        UnitKind.Series => new(
            "Bilgisayarındaki bir dizi klasörü.",
            "Bölümler bilgisayardan gider; başka yerde kopyan yoksa bir daha izleyemezsin.",
            "Hayır, ama karantinadan süresi içinde geri alabilirsin."),
        UnitKind.DevArtifact => new(
            "Projelerin derlenirken ürettiği geçici klasörler, örneğin node_modules ya da bin.",
            "Proje bir sonraki derlemede ya da kurulumda bunu yeniden üretir; kodun silinmez.",
            "Evet, derleme ya da paket kurulumuyla kendiliğinden geri gelir."),
        UnitKind.Cache => new(
            "Uygulamaların hızlı çalışmak için sakladığı geçici kopyalar.",
            "Uygulama ilk açılışta biraz yavaş olabilir; verin ve ayarların etkilenmez.",
            "Evet, uygulama kullandıkça kendiliğinden yeniden oluşturur."),
        UnitKind.BrowserCache => new(
            "Tarayıcının sitelerden sakladığı geçici dosyalar.",
            "Siteler ilk açılışta biraz yavaş yüklenir. Giriş yaptığın siteler açık kalır.",
            "Evet, tarayıcı gezdikçe kendiliğinden yeniden oluşturur."),
        UnitKind.Installer => new(
            "Daha önce indirilmiş bir kurulum dosyası.",
            "Program zaten kuruluysa hiçbir etkisi olmaz.",
            "Evet, yeniden kurmak isterseniz dosyayı tekrar indirirsiniz."),
        UnitKind.SystemArtifact => new(
            "Windows'un eski güncelleme ve kurulum artıkları.",
            "Bilgisayar olduğu gibi çalışır; yalnız eski sürüme dönüş seçeneği azalabilir.",
            "Hayır, ama Windows ihtiyaç duyduğunda yenisini kendisi oluşturur."),
        UnitKind.Folder => new(
            "Uzun süredir açılmamış büyük bir klasör.",
            "İçindekiler karantinaya taşınır; hemen yer açılır.",
            "Evet, karantina süresi içinde tek tıkla geri alırsınız."),
        UnitKind.OldDownload => new(
            "İndirilenler klasöründe uzun süredir duran eski bir dosya.",
            "Dosya karantinaya taşınır, kalıcı silinmez.",
            "Evet, karantina süresi içinde tek tıkla geri alırsınız."),
        UnitKind.CloudCopy => new(
            "Bulutta da bulunan ve bu bilgisayarda yer kaplayan bir kopya.",
            "Yalnız bu bilgisayardaki kopya kalkar; dosyalar bulutta durur.",
            "Evet, dosyayı açtığında internetten yeniden iner; bağlantı yoksa açılmaz."),
        UnitKind.Duplicate => new(
            "İçeriği birebir aynı olan birden çok dosya.",
            "Kalacak kopya dışındakiler karantinaya taşınır; bir kopya her zaman kalır.",
            "Evet, karantina süresi içinde tek tıkla geri alırsınız."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
