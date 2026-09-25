# Arayüz Düzeltme

Kaynak: `docs/dalga/arayuz-inceleme-1.md`. Sekiz bulgunun hepsine dokunuldu; yedisi tamamen düzeldi, biri (pasif düğme) zaten standarttaydı.

## Sonuç Özeti

- `dotnet build DustyBytes.slnx --no-incremental`: **0 hata, 0 uyarı**.
- `dotnet test DustyBytes.slnx`: **295 test, 294 yeşil, 1 kırmızı**. Kırmızı olan test arayüz dışında ve eskiden de kırmızıydı; gerekçesi aşağıda.
  - Units 39, Uninstall 105, Signals 24, Safety 44, Rules 27 ve App (DustyBytes.Tests) 38 testin hepsi yeşil.
  - Scan 17/18.
- Headless çekimler `tmp/uc/` altında: yedi ekran × üç ölçek (100/125/150). KontrastTests her ekranda 2/2 geçti.
- Gerçek pencere en küçük boyutta, prova kipinde (`DUSTYBYTES_DRYRUN=1`) açıldı; yakalanan pencere 864×679 piksel. Salt okunur yeniden tarama bittikten sonra çekildi (`tmp/gercek-pencere-2.png`) ve uygulama kapatıldı. `window.json`'a dokunulmadı, kapanıştan sonra da aynı.

## Bulgu 1 — Türe Göre: Oyun Ve Program Yok, Geliştirici Şişkin

**Kök neden iki parça.** Tanı konsolu (`tmp/turdokum/`) kanıtlıyor.

**(a)** AppBackend, oyun kütüphanesi köklerini (`C:\Games\Steam`, `Epic Games`) `AddLauncherLibrary` ile korumalı listeye ekliyor. UnitBuilder ise SystemArtifact dışındaki her birimi korumalı yolda reddediyordu. Yedi oyunun hepsi `verdict=False` ile düşüyordu.

**(b)** Program birimi planda tanımlıydı ama onu üreten çıkarıcı hiç yazılmamıştı. 199 kurulu programın hiçbiri birime dönüşmüyordu.

**Geliştirici 72,9 GB gerçek bir değer, yanlış sınıflama değil.** DevArtifact oyun klasörlerini yutmuyor:
- `AppData\Local\Temp\claude` altında 47,0 GB / 913 birim var (bin/obj).
- `Desktop\Projeler` altında 27,3 GB var.

**Düzeltme:**
- `src/DustyBytes.Units/UnitBuilder.cs:56,74`: korumalı yol kontrolü `Blocks(unit, verdict)` oldu. Yalnız başlatıcıya yönlendirilen birim (`Removal=Launcher`) başlatıcı rozetli kökten geçebilir. Karantinaya gidecek birimler aynı kökte hâlâ engelli.
- `src/DustyBytes.Units/ProgramExtractor.cs` (yeni): `ProgramInstall` kaydı ve çıkarıcısı. Tam nitelikli, var olan, boyutu sıfırdan büyük kurulum klasörü birim olur; birimde `Removal=Uninstaller` kullanılır. `UnitBuilder.cs:13`'te GameExtractor'dan sonra geliyor, yani aynı klasör için oyun kazanır.
- `src/DustyBytes.Units/UnitContext.cs:13`: `Programs` alanı eklendi.
- `src/DustyBytes.App/Services/UnitPrograms.cs` (yeni) ve `AppBackend.cs:82,88`: kurulu programlar filtrelenip birime veriliyor. Framework, kaldırılamayan, MSIX ve çalışma zamanı kütüphaneleri eleniyor.
- Test `tests/DustyBytes.Units.Tests/KindBreakdownTests.cs` dört vakayı kapsıyor:
  - Başlatıcı kökündeki oyun varsayılan kurulumda birim olur.
  - Aynı kök karantina birimini hâlâ engeller.
  - Hayalet ve göreli program yolları elenir.
  - Aynı klasörde oyun programı yener.

**Gerçek makinede önce ve sonra** (aynı önbellekli tarama: 902,4 GB, 2.892.802 dosya). Kanıtlar `tmp/turdokum/once.txt` ve `tmp/turdokum/sonra.txt`.

| Tür | Önce birim | Önce GB | Sonra birim | Sonra GB |
|---|---:|---:|---:|---:|
| Oyun | 0 | 0,0 | 7 | 148,3 |
| Geliştirici | 6.222 | 78,3 | 5.430 | 77,3 |
| Film ve dizi | 12 | 62,1 | 12 | 62,1 |
| Program | 0 | 0,0 | 61 | 31,7 |
| Klasör | 12 | 18,5 | 11 | 17,5 |
| Tarayıcı önbelleği | 3 | 7,2 | 3 | 7,2 |
| Önbellek | 2 | 0,3 | 2 | 0,3 |
| Sistem | 1 | 0,0 | 1 | 0,0 |
| Kurulum dosyası | 2 | 0,0 | 2 | 0,0 |
| **Toplam** | **6.254** | | **5.529** | |

Program hunisi: 199 kurulu program, 72 aday, 61 birim. Kalan 11 aday ya taranan ağaçta yok ya da bir oyunla aynı klasörde.

Titan Quest II (Epic) taranan ağaçta düğüm olarak yok; boyutu başlatıcının `SizeOnDisk` değerinden geliyor.

## Bulgu 2 — Bekleyen Karantinayı Okuyamıyor

**Neden:** Genel Bakış kendi sayacını tutuyordu. Açılışta gidilen ekranda karantina henüz okunmadığı için değer boş kalıyordu.

**Düzeltme:** tek kaynak `SessionState.PendingText` (`SessionState.cs:26,29`).
- `OverviewViewModel.cs:154` ve `QuarantineViewModel.cs:88` aynı değeri okuyor.
- Genel Bakış'a her gelişte karantina tazeleniyor (`OverviewViewModel.cs:162`).

**Test:** `ViewModelTests.Overview_Pending_Reads_Same_Quarantine_As_Quarantine_Screen`.

Gerçek pencerede değer "0 B, 0 öğe" çıktı; makinedeki karantina boş.

## Bulgu 3 — Harita Dilimleri Tür Rengini Almıyor

**Neden:** Renk yalnız birim yolunun kendisine ya da altına veriliyordu. Kök düzeyindeki `Oyunlar`, `Games` gibi klasörler "Diğer" rengine düşüyordu.

**Düzeltme:** `MapViewModel.cs:29,84-108,112-128`.
- Her birimin baytı üst klasörlerine tür bazında toplanıyor.
- Bir klasör boyutunun en az yarısı tek türdense o türün rengini alıyor.
- Dizi Film'e, tarayıcı önbelleği Önbellek'e katılıyor; böylece gösterge ile birebir eşleşiyor.
- Renkler mevcut tür paletinden geliyor, yeni renk yok.

**Mono boyut:** `TreemapControl.cs:146-160,202-205,248`. Boyut etiketi artık `FontMono` token ailesiyle çiziliyor; ad `FontSans` ile çiziliyor.

**Test:** `ViewModelTests.Map_Folder_Takes_Kind_Of_Units_Inside`. Görsel kanıt `tmp/uc/ana-100-map.png`.

## Bulgu 4 — Alt Satır Ayracı

**Neden:** Çok satırlı `<Run>` bloklarında satır sonu boşluğu fazladan boşluk üretiyordu ("Oyun ,  Son kullanım").

**Düzeltme:** Run'lar tek satıra alındı ve ayraç `<Run Text=" · "/>` oldu. Değişen yerler:
- `OverviewView.axaml:83`
- `ProgramsView.axaml:57`
- `QuarantineView.axaml:85`
- `UninstallView.axaml:81`

Alt çubuk metinleri de aynı ayracı kullanıyor: `OffersViewModel.cs:154`, `QuarantineViewModel.cs:101`, `MapViewModel.cs:70-79` ve `CleanupViewModel` TotalText.

## Bulgu 5 — Başlıklarda g Kuyruğu Kesiliyor

**Neden:** H3 20 px, LineHeight 24 (`LineHeightHeading` 1.2). Atkinson gömülü değil, Segoe UI'a düşüyor ve inen kuyruk TextBlock sınırından taşıyor.

**Düzeltme:** `AppStyles.axaml:393` global TextBlock stiline `ClipToBounds=False` eklendi. LineHeight token'ı değiştirilmedi.

**Doğrulama:** `tmp/zoom-g3.png` (150 %, "Sistem temizliği") ve `tmp/zoom-g4.png` (100 %, "Türe göre"). Kuyruklar tam.

## Bulgu 6 — 848×640'ta İlk Beş Teklif Kesik

**Neden:** "Türe göre" paneli artık Oyun ve Program satırlarıyla uzadı ve "İlk beş teklif"i ekranın altına itiyordu.

**Düzeltme:** `OverviewView.axaml:68,96`. "İlk beş teklif" paneli "Türe göre"nin üstüne alındı.

**Doğrulama:** `tmp/uc/ana-100-overview.png` ve `tmp/gercek-pencere-2.png`. Beş satırın hepsi ilk ekranda görünüyor.

Gerçek makinede ilk beş: DOOM: The Dark Ages 98,8 GB, Titan Quest II 28,6 GB, Wallpaper Engine 788 MB, ProcWitness.App 2 GB, Sun Haven 1,55 GB. Açılabilir alan 321 GB.

## Bulgu 7 — Sayı Biçimi

- **Binlik ayraç:** `Format.cs:25` içine `Format.Count` (tr-TR `N0` → "6.254") eklendi. Kullanıldığı yerler:
  - `OverviewViewModel.cs:132,135`
  - `ProgramsViewModel.cs:98`
  - `OffersViewModel.cs:154`
  - `QuarantineViewModel.cs:101`
  - `SessionState.cs:29,69`
  - `AppBackend.cs:72,161`
  - Test: `ViewModelTests.Counts_Use_Turkish_Thousands_Separator`.
- **Mono boyut:** alt çubuklarda boyut ayrı bir `Run` içinde, `FontMono` ailesiyle. Yerler:
  - `OffersView.axaml:31`
  - `QuarantineView.axaml:27`
  - `MapView.axaml:36`
  - `CleanupView.axaml:17`
  - Genel Bakış birim sayısı: `OverviewView.axaml:97`.

## Bulgu 8 — Hizalar, Tooltip, Pasif Düğme

- **Öneriler kart hizası:** `OffersView.axaml:61,77`. Kart ızgarası paylaşılan boyut kapsamında; onay kutusu sütunu `SharedSizeGroup="check"`. Kutusuz kartların başlığı da aynı sütundan başlıyor (`tmp/uc/ana-100-offers.png`).
- **Sistem temizliği iç boşluğu:** `CleanupView.axaml:84`. Diğer kartlarla aynı `Padding="16"` (satır 55).
- **Winapp2 açıklaması:** `CleanupViewModel.cs:47` (`SourceTip`) ve `CleanupView.axaml:60` (`ToolTip.Tip`). Metin, kuralın topluluk listesinden (CC-BY-SA-4.0) geldiğini ve korumalı listeden geçtiğini söylüyor.
- **Pasif düğme kontrastı: değişiklik yok, zaten standart.** Öneriler, Harita, Karantina ve Programlar'daki pasif düğmelerin pikselleri ölçüldü. Hepsi devre dışı token'ı `Disabled` #71717A (113,113,122) ile çiziliyor; tooltip ve `Cursor=No` mevcut.

## Eksik Token

- Harita metin boyutu `14` sabit: SkiaSharp çiziminde `FontSize1` değerine eşit; Double token'ı koddan okunmuyor.
- Treemap boşluğu ve köşe yarıçapı: `Gap` ve `Radius` ödünç.
- Kart `Padding="16"`: Thickness token'ı yok; mevcut kartlarla aynı sabit.

## Düzeltilemeyen / Kapsam Dışı

**`DustyBytes.Scan.Tests.FileScannerTests.InaccessibleDirectoryIsFlagged`:** bu düzeltmeden önce de kırmızıydı.
- Fikstür `denied` klasörünün ACL'ini reddediyor (`ScanFixture.cs:48-50`), sonra aynı klasörü `CreateFileW` ile kilitlemeye çalışıyor (`ScanFixture.cs:117`).
- Kendi reddettiği ACL yüzünden tutamaç geçersiz dönüyor.
- Tarama motorunun fikstür hatası; arayüz kapsamında değil, dokunulmadı.

**Derleme uyarıları:** 0 uyarı hedefi için arayüz dışı üç küçük değişiklik yapıldı.
- `MsixPackages.cs:43`: CA1416, sürüm denetimi lambda içine alındı.
- `MiscTests.cs:82`: xUnit1031, test `async` yapıldı.
- `DustyBytes.Tests.csproj`: IL2075; test projesinde `IsAotCompatible=false`.
