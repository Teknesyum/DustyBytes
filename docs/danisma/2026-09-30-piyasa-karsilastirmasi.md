# Piyasa Karşılaştırması Ve Eksik Listesi — 2026-09-30

Hedef: DustyBytes'ı teknik bilgisi hiç olmayan kullanıcı için piyasadaki en pratik, en hızlı ve en
kolay temizleyici yapmak. Bu kağıt bugünkü durumu dosyalardan doğrular, rakipleri web kaynaklarıyla
karşılaştırır ve eksikleri sıralar. Hiçbir kod değişmedi.

## DustyBytes Bugün Ne Yapıyor

Kaynak: `README.md`, `docs/YOL-HARITASI.md`, `docs/plan.md` (A9–A14), `src/` taraması.

- **Tarama:** `FindFirstFileEx` (yetkisiz) ve MFT okuyucu (yönetici); USN günlüğüyle artımlı yenileme
  varsayılan (A12). Tek makinede `C:\` MFT ile 8,6 sn, 2,84 M dosya.
- **Yalnız sistem sürücüsü taranıyor:** `AppBackend.ScanRoot = Path.GetPathRoot(Environment.SystemDirectory)`.
  Oyunlar başlatıcı manifestinden geldiği için D:'deki Steam oyunu görünür; D:'deki film, klasör, kurulum dosyası görünmez.
- **Amaca göre birim:** oyun, program, uygulama içeriği (LM Studio, Ollama vb. 24 bilinen yer), film, dizi,
  geliştirici artığı, önbellek, tarayıcı önbelleği, indirilen kurulum dosyası, sistem artığı, klasör.
- **Son kullanım:** Steam/Epic/GOG, Prefetch, UserAssist, oynatıcı geçmişi; kaynağı ve güveni gösterilir.
- **Akışla sonuç:** ağaçtan bağımsız türler tarama sürerken kesin boyutla görünür (A12).
- **Silme:** tek tık karantina (aynı birimde yeniden adlandırma, 7 gün, Geri al bildirimi); kalıcı silme
  iki basışla (`TwoStep`, A14); her istek yönetici worker'da korumalı listeden geçer.
- **Kalıntısız kaldırma (A13):** sessiz kaldırıcı (MSI, Inno, NSIS, Burn, Squirrel), önce/sonra anlık görüntü,
  Yüksek güvenli kalıntı `.reg` yedeği ve karantinayla onaysız, geri yükleme noktası, COM ve kabuk uzantıları.
- **Temizlik:** 23 JSON kural (tarayıcılar, Windows temp, çökme dökümleri, shader önbellekleri, Discord, Teams…),
  isteğe bağlı `winapp2.ini`, DISM `/StartComponentCleanup` (`/ResetBase` yasak), Windows Update önbelleği,
  Delivery Optimization, `Windows.old`.
- **Otomatik mod ve tur (A14):** "Otomatik temizle" sorusuz kategorileri temizler, sonra büyük ve kullanılmayan
  birimleri tek tek sorar (Karantinaya al / Kalıcı sil / Kalsın / Turu bitir), bitişte özet verir.
- **Arayüz:** ekranlar `OverviewView`, `OffersView`, `MapView` (treemap), `ProgramsView`, `UninstallView`,
  `CleanupView`, `QuarantineView`, `TourView`, `TaskProgressView`; arayüz yakınlaştırma (`UiZoom`);
  güncelleme rozeti SHA-256 doğrulamalı.
- **Yok (kodda arandı, bulunmadı):** yinelenen dosya, Geri Dönüşüm Kutusu boşaltma, zamanlanmış çalışma,
  düşük disk bildirimi, bildirim alanı simgesi, hazırda bekletme önerisi, dosya sıkıştırma, başlangıç yöneticisi,
  Explorer sağ tık menüsü, zorla kaldırma, toplu kaldırma, çoklu sürücü, İngilizce arayüz (A8 ertelendi),
  kod imzası (A7'de açık).

## Özellik Matrisi

`✓` var, `~` kısmen ya da ücretli sürümde, `—` yok, `?` doğrulanamadı.
PC Manager sütunu Windows 11 Storage Sense ve Temizleme Önerileri ile birlikte okunur.

| Özellik | DustyBytes | CCleaner 7 | BleachBit 5 | WizTree | WinDirStat 2.x | Revo Pro 5 | BCU 6 | PC Manager + Storage Sense | Wise Disk Cleaner 11 |
|---|---|---|---|---|---|---|---|---|---|
| MFT ile hızlı tarama | ✓ | — | — | ✓ | ✓ | — | — | — | — |
| Amaca göre gruplama (oyun, film, model) | ✓ | — | — | — | — | — | ~ (Steam) | — | — |
| Son kullanım tarihi | ✓ | — | — | ~ (dosya tarihi) | — | ? | ~ | ~ (kullanılmayan uygulama) | — |
| Büyük dosya bulucu | ~ (birim, ≥1 GB) | ✓ | — | ✓ | ✓ | — | — | ✓ | — |
| Yinelenen dosya | — | ✓ | — | ✓ | ✓ | — | — | ✓ | — |
| Tarayıcı önbelleği | ✓ | ✓ | ✓ | — | — | ~ | — | ✓ | ✓ |
| Windows Update / bileşen deposu | ✓ | ✓ | ? | — | — | — | — | ✓ | ✓ |
| Geri Dönüşüm Kutusu | — | ✓ | ✓ | — | — | ? | — | ✓ | ✓ |
| İndirilenler klasörü temizliği | ~ (yalnız kurulum dosyası) | — | — | — | — | — | — | ✓ | — |
| Tüm sürücüler | — | ~ | ~ | ✓ | ✓ | — | ~ (taşınabilir) | ✓ | ✓ |
| Zamanlanmış temizlik | — | ~ (Pro) | — | — | — | — | — | ✓ | ✓ |
| Disk azalınca bildirim / otomatik | — | ~ (Pro, Smart Cleaning) | — | — | — | — | — | ✓ | — |
| Geri alma (karantina) | ✓ (7 gün) | — | — | ~ (Geri Dönüşüm) | ~ (Geri Dönüşüm) | ~ (yedek) | ~ (geri yükleme noktası) | — | — |
| Kalıntısız kaldırma | ✓ | ~ | — | — | — | ✓ | ✓ | — | — |
| Zorla kaldırma | — | — | — | — | — | ✓ | ✓ | — | — |
| Toplu kaldırma | — | — | — | — | — | ? | ✓ | — | — |
| Başlangıç yöneticisi | — | ✓ | — | — | — | ✓ | ✓ | ✓ | — |
| Saydam sıkıştırma (oyun küçültme) | — | — | — | — | ~ (gösterir) | — | — | — | — |
| Reklam ve satış baskısı yok | ✓ | — | ✓ | ✓ | ✓ | ~ | ✓ | ✓ | ~ |
| Açık kaynak | ✓ (AGPL) | — | ✓ | — | ✓ | — | ✓ | — | — |
| Kod imzalı | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Çok dilli | — (yalnız Türkçe) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

**Kullanıcı sesi.** CCleaner'dan göçün sebebi işlev değil güven: açılır pencere, arka plan hizmeti, telemetri,
satış baskısı. IObit için kurulumda işaretli gelen ek ürünler ve abonelik şikâyeti öne çıkıyor. BleachBit
reklamsız olduğu için övülüyor ama arayüzü teknik. CleanMyMac'in övülen yanı tek ekranda karo özet ve tek tık
"Smart Care". DustyBytes güven tarafında (reklam yok, telemetri yok, karantina) zaten önde; açık alanlar
otomasyon, kapsam (sürücüler, çöp kutusu, kopyalar) ve ilk açılış güveni (imza).

## Sıralı Eksik Listesi

Değer: teknik bilgisi olmayan kullanıcıya faydası (1–5). Emek: S (gün), M (hafta), L (birkaç hafta).
Risk: kullanıcı verisine risk. **Hızlı kazanç** = S ya da M emek ve düşük risk, değeri 3 ve üstü.

| # | Eksik | Kimde var | Değer | Emek | Risk | Etiket |
|---|---|---|---|---|---|---|
| 1 | Bütün sürücüleri tara | WizTree, WinDirStat, PC Manager | 5 | M | düşük | hızlı kazanç |
| 2 | Disk azalınca bildirim ve haftalık kontrol | Storage Sense, CCleaner Pro, Wise | 5 | M | düşük | hızlı kazanç |
| 3 | Kod imzası (SmartScreen uyarısı kalkar) | Hepsi | 5 | S | düşük | hızlı kazanç |
| 4 | Geri Dönüşüm Kutusu (30 günden eski) | Storage Sense, CCleaner, BleachBit, Wise | 4 | S | düşük | hızlı kazanç |
| 5 | İndirilenler'de uzun süre açılmamış dosyalar | PC Manager, Storage Sense | 4 | S | düşük | hızlı kazanç |
| 6 | Oyun ve programı silmeden küçült (saydam sıkıştırma) | CompactGUI (tek amaçlı) | 4 | M | düşük | hızlı kazanç |
| 7 | OneDrive'daki eski dosyaları "yalnız çevrimiçi" yap | Storage Sense | 4 | M | düşük | hızlı kazanç |
| 8 | Yinelenen dosya bulucu | PC Manager, CCleaner, WizTree, WinDirStat, Glary | 4 | M | orta | |
| 9 | Disk doluyken "şimdi yer aç" (karantinayı erken boşalt) | Storage Sense (disk azalınca çalışır) | 4 | S | orta | |
| 10 | Hazırda bekletme dosyası önerisi | Elle; rehberler | 3 | S | düşük | hızlı kazanç |
| 11 | Geçmiş ve önce/sonra raporu | CleanMyMac, CCleaner bildirimi | 3 | S | düşük | hızlı kazanç |
| 12 | Explorer sağ tık: "Bu klasörü incele", "Temiz kaldır" | WinDirStat, Revo (Hunter), IObit | 3 | S | düşük | hızlı kazanç |
| 13 | Başlangıç programları (aç/kapa) | CCleaner, BCU, Revo, PC Manager, Glary | 3 | M | düşük | hızlı kazanç |
| 14 | Zorla kaldırma (kaldırıcısı bozuk program) | Revo, BCU, IObit | 3 | M | orta | |
| 15 | Toplu kaldırma | BCU | 3 | M | orta | |
| 16 | Büyük birimi başka sürücüye taşı | Steam (elle), Windows "Taşı" | 3 | L | orta | |
| 17 | İngilizce arayüz (A8) | Hepsi | 2 | M | düşük | |
| 18 | Gölge kopya ve geri yükleme noktası alanını göster | Disk Temizleme "Diğer seçenekler" | 2 | S | orta | |
| 19 | Tarayıcı kapanınca önbelleği temizle | CCleaner Smart Cleaning | 2 | M | düşük | |
| 20 | Paket yazılım ve tarayıcı eklentisi tespiti | IObit Uninstaller | 2 | L | orta | |

### Ayrıntı Ve Kod İpucu

1. **Bütün sürücüleri tara.** D: ve harici diskteki film, klasör ve eski kurulum dosyası bugün hiç görünmüyor;
   kullanıcı "neden E: dolu" sorusunu soramaz. *İpucu:* `AppBackend.ScanRoot` tek kök; `DriveInfo.GetDrives()`
   ile sabit ve çıkarılabilir birimleri listele, sürücü başına `ScanIndex` ve USN imleci tut; karantina zaten
   birim başına (`X:\.dustybytes`), birimler arası taşıma gerekmez.
2. **Disk azalınca bildirim ve haftalık kontrol.** Teknik olmayan kullanıcı programı hatırlamaz; Storage Sense
   bunu kendiliğinden yapıyor. *İpucu:* kullanıcı düzeyinde (yönetici değil) zamanlanmış görev
   `DustyBytes.exe --check` yalnız boş alanı ölçer ve Windows bildirimi gösterir: "C: %92 dolu, 14 GB açılabilir";
   temizlik ancak tıklayınca açılan pencerede. A9'daki "yönetici görevi yok" kuralına uyar, sürekli çalışan süreç yok.
3. **Kod imzası.** İlk açılışta SmartScreen'in "tanınmayan uygulama" ekranı, teknik olmayan kullanıcıyı
   programdan önce kaybettirir. *İpucu:* A7'de açık kalan sahip kararı; bulut imza hizmeti (ör. Azure Trusted
   Signing) ile yayın iş akışına `signtool` adımı, `Kur.bat` ve zip içindeki exe imzalı.
4. **Geri Dönüşüm Kutusu.** Kullanıcının zaten sildiği dosyalar; rakiplerin hepsi temizliyor. *İpucu:*
   `SHQueryRecycleBin` ile boyut, `ISystemCleanupTask` altında `recycle-bin`; plan zaten `SCID_DATE_DELETED`
   okuyor, yalnız 30 günden eskiler varsayılan işaretli. `rules/protected.json`'daki çöp kutusu kaydı
   worker'da bu göreve izin verecek şekilde ayrılmalı.
5. **İndirilenler'de eski dosyalar.** En çok unutulan klasör; PC Manager ayrı sekme yapmış. *İpucu:*
   `InstallerExtractor`'ı genişlet: 90 günden uzun açılmamış her dosya "İndirilenler · eski" birimi, karantinayla;
   `IncludeUserData` bayrağı A9'daki gibi yalnız kullanıcı tıklayınca.
6. **Saydam sıkıştırma.** Oyunu silmek istemeyen kullanıcıya üçüncü yol: dosyalar aynı kalır, %30–60 yer açılır,
   geri alınabilir. DustyBytes oyunu zaten tanıyor; kartta ikinci eylem doğal. *İpucu:* worker'da
   `WofSetFileDataLocation` (XPRESS8K) ile yeni `Ops.Compress`; oyun ve program kartında "Küçült (≈x GB kazanç)";
   geri al aynı API ile sıkıştırmayı kaldırır. Yalnız NTFS, sık yazılan klasörler (kayıtlar) hariç.
7. **OneDrive yalnız çevrimiçi.** Dizüstü kullanıcısında en büyük kalemlerden biri; dosya bulutta kalır, silinmez.
   *İpucu:* bulut yer tutucu tespiti tarayıcıda var; indirilmiş ve uzun süre açılmamış dosyalara
   `CfSetPinState(CF_PIN_STATE_UNPINNED)` ile "Bulut kopyası" birimi. Senkron hatası varsa teklif edilmez.
8. **Yinelenen dosya.** Fotoğraf ve video kopyaları gerçek yer yer; ama yanlış kopyayı silmek veri kaybı.
   *İpucu:* plan §3'te ertelenen algoritma hazır (boyut → 4 KB önek hash → tam hash, >10 MB, işlem anında
   yeniden doğrula). `DuplicateExtractor` birimi, "en eski yoldaki kalsın" varsayılanı, yalnız karantina.
9. **Disk doluyken şimdi yer aç.** Karantina 7 gün yer açmaz; disk %98 doluyken bu kullanıcıyı şaşırtır.
   *İpucu:* Genel bakışta boş alan %10'un altındaysa "Bekleyen x GB'ı şimdi kalıcı sil" tek düğmesi,
   `TwoStep` iki basışlı; worker `purge` zaten var.
10. **Hazırda bekletme.** `hiberfil.sys` RAM'in %40'ı kadar; kapatınca Hızlı Başlangıç gider, tamamen geri alınabilir.
    *İpucu:* korumalı listedeki "sistem, ayarlardan" kaydını bir öneri kartına çevir; worker `powercfg /h off`,
    geri al `powercfg /h on`; dizüstünde (pil var) uyarı cümlesiyle, varsayılan önerme yalnız masaüstünde.
11. **Geçmiş ve önce/sonra.** "Bu ay 12 GB açtınız" güven verir, programa geri getirir. *İpucu:* tur özetindeki
    ve `SessionState`'teki "Açılan" toplamını tarihli SQLite tablosuna yaz; Genel bakışta önce/sonra disk çubuğu.
12. **Explorer sağ tık.** Kullanıcı klasörü zaten Explorer'da görüyor. *İpucu:* kurulum betiği
    `HKCU\Software\Classes\Directory\shell\DustyBytes` (yönetici gerekmez) → `DustyBytes.exe --inspect "%1"`;
    `.lnk` için "Temiz kaldır" `Programs` ekranında o programı açar.
13. **Başlangıç programları.** Disk dışı ama "bilgisayar yavaş" diyen kullanıcının ilk beklentisi. *İpucu:*
    `LeftoverScanner` Run ve `StartupApproved` değerlerini zaten okuyor; aç/kapa yalnız `StartupApproved`
    baytını değiştirir, geri alınabilir. Kapsam kayması: sahip karar verir.
14. **Zorla kaldırma.** A13 "Sonra" listesinde. *İpucu:* kaldırıcı yoksa ya da hata verirse `InstallLocation`
    ve yayıncı kimliğiyle `LeftoverScanner` doğrudan çalışır; iki çapa kuralı aynen geçerli.
15. **Toplu kaldırma.** *İpucu:* `ProgramsView`'da çoklu seçim, sessiz kaldırıcılar sırayla; sessiz olmayan
    kaldırıcıda kuyruk durur ve "sihirbazı siz bitirin" kartı gösterilir.
16. **Başka sürücüye taşı.** Silmek istemeyen için. *İpucu:* oyunda başlatıcının kendi taşıma akışına yönlendir;
    film ve klasörde kopyala-doğrula-sil ayrı bir işlem olarak (karantina kuralı birimler arası kopyayı yasaklıyor,
    bu ayrı olmalı).
17. **İngilizce arayüz.** Türk kullanıcı için değer düşük, pazar için yüksek. A8 planı yeterli.
18. **Gölge kopya alanı.** Yalnız gösterilir, silinmez; "Sistem Koruması" ayarına yönlendirir
    (`vssadmin list shadowstorage`). Geri yükleme noktası silmek veri riskidir.
19. **Tarayıcı kapanınca temizle.** Sürekli çalışan süreç ister; "hizmet yok" kararıyla çelişir. Düşük öncelik.
20. **Paket yazılım ve eklenti.** Veritabanı gerektirir, yanlış pozitif riski yüksek; düşük öncelik.

## Dışarıda Bırakılanlar (Snake-Oil Ya Da Kapsam Dışı)

- **Kayıt defteri temizleyici:** Microsoft desteklemiyor, hız kazandırmaz, sistemi bozabilir. README'de zaten yok.
- **RAM hızlandırıcı / bellek boşaltıcı:** Windows belleği kendisi yönetir; boşaltılan önbellek geri yüklenir, yavaşlatır.
- **Sürücü güncelleyici:** Windows Update aynı işi yapar; doğrulanmamış sürücü kurulumu kararsızlık getirir.
- **"Performans optimize edici" (uygulamaları uyutma):** ölçülebilir kazanç iddiası belirsiz, arka plan süreci ister.
- **SSD'de birleştirme (defrag):** SSD'ye fayda yok, yazma ömrü harcar; Windows kendi optimizasyonunu yapar.
- **Güvenli silme / boş alan silme:** SSD'de TRIM ve aşınma dengeleme yüzünden güvence vermez; hedef kullanıcıya fayda yok.
- **Kırmızı "sağlık puanı" alarmları:** korkutma deseni; CCleaner ve IObit şikâyetlerinin kaynağı. Tek ekran özet evet, alarm hayır.

## Kaynaklar

- CCleaner sürüm notları: https://www.ccleaner.com/ccleaner/version-history
- CCleaner Free: https://www.ccleaner.com/ccleaner-free
- CCleaner Smart Cleaning: https://support.ccleaner.com/articles/en_US/Master_Article/what-is-the-smart-cleaning-feature
- CCleaner 2026 inceleme: https://cybernews.com/privacy-tools/ccleaner-review/
- CCleaner şikâyetleri ve alternatifler: https://storedbits.com/ccleaner-alternative/ , https://dev.to/larop6547/still-using-ccleaner-in-2025-heres-what-i-think-plus-3-alternatives-i-actually-like-1o33
- BleachBit 5.0.0: https://www.bleachbit.org/news/bleachbit-500
- WizTree: https://diskanalyzer.com/about , https://www.makeuseof.com/use-wiztree-storage-tool-windows/
- WinDirStat 2.x: https://github.com/windirstat/windirstat/blob/master/CHANGELOG.md , https://alternativeto.net/news/2026/1/windirstat-2-5-adds-dark-mode-ntfs-mft-scanning-improved-search-and-new-file-actions/
- TreeSize: https://www.jam-software.com/treesize/features.shtml , https://www.jam-software.com/treesize/find-duplicate-files.shtml
- Revo Uninstaller Pro: https://www.revouninstaller.com/products/revo-uninstaller-pro/
- Bulk Crap Uninstaller: https://www.bcuninstaller.com/ , https://www.techradar.com/pro/bulk-crap-uninstaller-review
- IObit Uninstaller 15: https://www.techspot.com/downloads/4959-iobit-uninstaller.html
- IObit Advanced SystemCare şikâyetleri: https://www.trustpilot.com/review/www.iobit.com , https://www.techradar.com/reviews/iobit-advanced-systemcare-free
- Wise Disk Cleaner: https://www.wisecleaner.com/wise-disk-cleaner.html , https://www.itechguides.com/products/wise-disk-cleaner/
- Glary Utilities: https://thectoclub.com/tools/glary-utilities-review/
- Microsoft PC Manager: https://pcmanager.microsoft.com/en-us , https://www.windowslatest.com/2024/05/11/microsoft-pc-manager-performance-booster-for-windows-11-gets-files-cleanup/ , https://www.makeuseof.com/windows-storage-cleanup-with-pc-manager/
- Storage Sense ve Temizleme Önerileri: https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/manage-drive-space-with-storage-sense , https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows , https://www.thewindowsclub.com/storage-sense-in-windows-11
- CleanMyMac arayüzü: https://www.macworld.com/article/352922/cleanmymac-x-review-macos.html , https://thesweetbits.com/tools/cleanmymac-review/
- CompactGUI (saydam sıkıştırma): https://github.com/IridiumIO/CompactGUI , https://www.xda-developers.com/free-open-source-tool-compress-games-windows-compactgui/
- Hazırda bekletme: https://www.howtogeek.com/868748/how-to-disable-hibernation-on-windows-10/ , https://learn.microsoft.com/en-us/answers/questions/5780091/how-to-remove-hibernation-file-to-save-space
- Kayıt defteri temizleyici politikası: https://support.microsoft.com/en-us/help/2563254/microsoft-support-policy-for-the-use-of-registry-cleaning-utilities , https://www.xda-developers.com/stop-using-third-party-registry-cleaners-windows-11/
- Sürücü güncelleyiciler: https://www.howtogeek.com/stop-installing-driver-updaters-on-your-windows-pc/ , https://www.xda-developers.com/stop-using-drivers-update-software/
