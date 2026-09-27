namespace DustyBytes.Clean.Uninstall;

public static class Confidence
{
    public const int HighThreshold = 16;
    public const int MediumThreshold = 8;

    public static Evidence InstallDirExact(string path) =>
        new("install-dir", 20, AnchorClass.InstallFolder, $"Kurulum klasörünün kendisi: {path}");

    public static Evidence InstallDirInferred(string path) =>
        new("install-dir-inferred", 14, AnchorClass.InstallFolder, $"Kurulum klasörü, kaldırıcının konumundan: {path}");

    public static Evidence InsideInstallDir(string path) =>
        new("inside-install-dir", 12, AnchorClass.InstallFolder, $"Hedefi kurulum klasöründe: {path}");

    public static Evidence ReferencesInstallDir(string value) =>
        new("refs-install-dir", 12, AnchorClass.InstallFolder, $"Değeri kurulum klasörünü gösteriyor: {value}");

    public static Evidence ProgramShortcut() =>
        new("program-shortcut", 0, AnchorClass.None, "Programın kendi kısayolu: hedefi InstallLocation altında, Başlat menüsünde ya da masaüstünde");

    public static Evidence UninstallKeySelf() =>
        new("uninstall-key", 20, AnchorClass.UninstallKey, "Programın kendi Uninstall anahtarı");

    public static Evidence CompanyMatch(string company) =>
        new("company", 6, AnchorClass.VersionResource, $"Sürüm kaynağı CompanyName: {company}");

    public static Evidence ProductMatch(string product) =>
        new("product", 6, AnchorClass.VersionResource, $"Sürüm kaynağı ProductName: {product}");

    public static Evidence CompanyMismatch(string company) =>
        new("company-mismatch", -4, AnchorClass.None, $"Sürüm kaynağı başka şirketi gösteriyor: {company}");

    public static Evidence SignerMatch(string signer, bool trusted) =>
        new("signer", trusted ? 8 : 5, AnchorClass.Signer, $"Authenticode imzalayan: {signer}{(trusted ? "" : " (imza doğrulanamadı)")}");

    public static Evidence PublisherMatch(string name) =>
        new("publisher", 4, AnchorClass.Publisher, $"Üst klasör ya da anahtar yayıncı adı: {name}");

    public static Evidence NameExact(string name) =>
        new("name-exact", 6, AnchorClass.Name, $"Ad programla aynı: {name}");

    public static Evidence NamePartial(string name) =>
        new("name-partial", 3, AnchorClass.Name, $"Ad programla benzer: {name}");

    public static Evidence InstallTime() =>
        new("install-time", 3, AnchorClass.InstallTime, "Oluşturulma tarihi kurulum gününe yakın");

    public static Evidence OtherProgramName(string other) =>
        new("other-program", -6, AnchorClass.None, $"Başka kurulu program da bu adla eşleşiyor: {other}");

    public static Evidence StoreApp() =>
        new("store-app", -10, AnchorClass.None, "Store paketi; PackageManager ile kaldırılır");

    public static Evidence ComServerInDir(string path) =>
        new("com-server", 12, AnchorClass.InstallFolder, $"COM sunucusu kurulum klasöründe: {path}");

    public static Evidence LinkedTo(string what) =>
        new("linked", 12, AnchorClass.InstallFolder, $"Kurulum klasörünü gösteren girdiye bağlı: {what}");

    public static Evidence UserChoiceArea() =>
        new("user-choice", 0, AnchorClass.None, "Dosya ilişkisi seçimi; kullanıcı başka programa atayabilir, işaretsiz gelir");

    public static Evidence DriverService() =>
        new("driver", 0, AnchorClass.None, "Sürücü hizmeti; işaretsiz gelir");

    public static Evidence NoNameForClass() =>
        new("class-no-name", 0, AnchorClass.None, "Sınıf adı programın adını taşımıyor; işaretsiz gelir");

    public static Evidence SettingsFolder() =>
        new("settings", 0, AnchorClass.None, "Programın ayar ve verileri");

    static readonly HashSet<string> PathCodes = new(StringComparer.Ordinal)
    {
        "install-dir", "install-dir-inferred", "inside-install-dir", "refs-install-dir", "uninstall-key", "program-shortcut", "com-server", "linked",
    };

    static readonly HashSet<string> CapCodes = new(StringComparer.Ordinal) { "user-choice", "driver", "class-no-name" };

    public static bool HasPathEvidence(IReadOnlyCollection<Evidence> evidence) =>
        evidence.Any(e => PathCodes.Contains(e.Code))
        || evidence.Any(e => e.Code == "signer") && evidence.Any(e => e.Code is "company" or "product");

    public static bool IsCapped(IReadOnlyCollection<Evidence> evidence) =>
        evidence.Any(e => CapCodes.Contains(e.Code));

    public static (int Score, int Anchors, ConfidenceTier Tier) Evaluate(IReadOnlyCollection<Evidence> evidence)
    {
        var score = evidence.Sum(e => e.Points);
        var anchors = evidence.Where(e => e.Points > 0 && e.Anchor != AnchorClass.None).Select(e => e.Anchor).ToHashSet();
        if (evidence.Any(e => e.Code == "other-program"))
            anchors.Remove(AnchorClass.Name);
        var count = anchors.Count;
        var tier = count < 2 ? ConfidenceTier.Low
            : score >= HighThreshold ? ConfidenceTier.High
            : score >= MediumThreshold ? ConfidenceTier.Medium
            : ConfidenceTier.Low;
        if (tier == ConfidenceTier.High && IsCapped(evidence))
            tier = ConfidenceTier.Medium;
        return (score, count, tier);
    }

    public static string TierText(ConfidenceTier tier) => tier switch
    {
        ConfidenceTier.High => "Yüksek",
        ConfidenceTier.Medium => "Orta",
        _ => "Düşük",
    };

    public static string Reason(IReadOnlyCollection<Evidence> evidence, int anchors, ConfidenceTier tier)
    {
        var head = $"{TierText(tier)} ({anchors} çapa)";
        if (anchors < 2)
            head += ", iki bağımsız kanıt yok";
        return head + ": " + string.Join("; ", evidence.OrderByDescending(e => e.Points).Select(e => e.Text));
    }
}
