# Arayüz Ve Bağlama Dalgası

Altı ekranlı kabuk, kaldırma ekranı, servis bağlamaları, testler. Gerçek silme, kaldırma ve DISM çalıştırılmadı; uygulama yalnız `DUSTYBYTES_DRYRUN=1` ile açıldı.

## Başlatma Komutu

Ekran görüntüsü için (PowerShell 5.1, proje kökünden):

```powershell
$env:DUSTYBYTES_DRYRUN="1"; dotnet run --project src/DustyBytes.App
```

Derlenmiş exe: `src\DustyBytes.App\bin\Debug\net10.0-windows10.0.19041.0\DustyBytes.exe`. Başlık çubuğunda "Prova kipi: hiçbir dosya silinmez" rozeti görünür.

## Etki Bloğu

| dosya:satır | kural | değişiklik |
|---|---|---|
| `src/DustyBytes.App/Views/MainWindow.axaml:10` | özel başlık çubuğu | `ExtendClientAreaToDecorationsHint`, `NoChrome`, `TitleBarHeightHint=-1` |
| `src/DustyBytes.App/Views/MainWindow.axaml.cs:35` | Snap düzeni | büyüt düğmesine `Win32HitTestValue.MaxButton` |
| `src/DustyBytes.App/Views/MainWindow.axaml.cs:154` | sürükle / çift tık | `BeginMoveDrag`, `ClickCount==2` büyüt; düğme üstünde yok sayılır |
| `src/DustyBytes.App/Views/MainWindow.axaml:37` | imza | `SignatureView` küçült düğmesinin solunda (test ölçüyor) |
| `src/DustyBytes.App/Views/MainWindow.axaml:54` | kenar çubuğu | altı ekran, `TabNavigation=Once` (roving focus) |
| `src/DustyBytes.App/Views/MainWindow.axaml:72` | sayfa geçişi | `TransitioningContentControl`, `CrossFade(TBase)`; azaltılmış harekette null |
| `src/DustyBytes.App/Views/MainWindow.axaml.cs:28` | MinWidth token'dan | `SidebarWidth + ModalWidth + 2×Space5` = 848 |
| `src/DustyBytes.App/Views/MainWindow.axaml.cs:97` | pencere yeri | kayıtlı yer ekran dışındaysa ortala, çalışma alanına sığdır |
| `src/DustyBytes.App/Views/MainWindow.axaml.cs:135` | seyreltilmiş kayıt | `window.json` `TSlow` gecikmeli, kapanışta hemen |
| `src/DustyBytes.App/App.axaml.cs` | gizli açılış | pencere boyutu `AttachStore` ile kurulduktan sonra gösterilir |
| `src/DustyBytes.App/Program.cs:13` | tek örnek | ikinci açılış ilk pencereyi öne getirir, kod 0 ile çıkar |
| `src/DustyBytes.App/ViewModels/ViewModelBase.cs` | gezinme yığını | `NavigationStack`, geri dönüş |
| `src/DustyBytes.App/Services/ReducedMotion.cs` | azaltılmış hareket | Windows ayarı ya da `DUSTYBYTES_REDUCED_MOTION` → `anim` sınıfı |
| `src/DustyBytes.App/Views/OverviewView.axaml:16` | önbellek + tazele | önceki tarama "Tazeleniyor" rozetiyle, arkada yeni tarama |
| `src/DustyBytes.App/Views/OverviewView.axaml:20` | hızlı tarama | worker yoksa kapalı, `ToolTip.ShowOnDisabled` ile gerekçe |
| `src/DustyBytes.App/Services/AppBackend.cs:21` | UsageIndex | `Lazy<Task<UsageIndex>>` |
| `src/DustyBytes.App/Services/AppBackend.cs:73` | UnitBuilder | tarama sonucu birimlere |
| `src/DustyBytes.App/Services/AppBackend.cs:224` | tembel WorkerClient | ilk yıkıcı istekte yükseltilmiş worker açılır |
| `src/DustyBytes.App/ViewModels/OffersViewModel.cs:231` | toast + Geri al | karantina sonrası geri alma |
| `src/DustyBytes.App/ViewModels/MainViewModel.cs:117` | onay yalnız geri dönülmezde | doğrudan silme, kullanıcı verisi, kalıcı silme, kaldırma |
| `src/DustyBytes.App/Assets/AppStyles.axaml:392` | `+tnum` | tüm `TextBlock`'larda tablo rakamları |
| `src/DustyBytes.App/Controls/TransformAnimator.cs` | yalnız opacity/transform | Theme'deki `appbg` RenderTransform animasyonu için animatör |
| `src/DustyBytes.App/Views/*View.axaml` | ilerleme / boş / hata | yedi ekranın her birinde üç durum paneli |
| `src/DustyBytes.Clean/Uninstall/LeftoverScanner.cs:278` | motor değişikliği | kaldırılan programın `InstallLocation`'ına giden Başlat Menüsü / Masaüstü `.lnk` kalıntı adayı (Yüksek güven) |
| `src/DustyBytes.Worker/WorkerBindings.cs:70` | worker bağlama | uninstall, leftovers, clean, system-clean, fast-scan işleyicileri |
| `.claude/teknesyum-ui.json` | scan.js ignore | 8 kayıt, gerekçeli (aşağıda) |

## Uygulanmayan Kurallar Ve Gerekçe

- **`forms/no-ui-string-literal` (28 uyarı):** tek dilli Türkçe uygulama, planda yerelleştirme yok. `locale/tr.json`'a taşımak `en.json` eşliği de ister; bu ürün kararı. Dosya düzeyinde gerekçeli ignore.
- **`okunurluk/pair-contrast` AppStyles.axaml:297:** yanlış eşleşme. NeonBlue kutudaki işaret OnBlue ile çizilir (satır 279), TextBody kutunun dışındaki etikete gider. KontrastTests altı ekranda üç ölçekte geçiyor. Satır düzeyinde ignore.
- **24px hedef, ScrollBar içi:** Fluent ScrollBar'ın `PART_LineUp`/`PageUp` RepeatButton'ları 16px. Test ScrollBar alt öğelerini dışarıda bırakıyor; kaydırma tekerlek ve klavyeyle de yapılır.
- **Geçiş yalnız opacity/transform, ScrollBar içi:** Fluent Thumb'daki `CornerRadiusTransition` hariç tutuldu; uygulamanın kendi stillerinde başka özellik geçişi yok.
- **Bileşen deposu (DISM):** görev listede görünür ama kapalı, ToolTip "Ölçümü DISM çalıştırır; arayüz bu görevi başlatmaz" (`AppBackend.cs:180`).
- **xunit v3 yerine 2.9.3:** `Avalonia.Headless.XUnit` 11.3.20 xunit 2'ye bağlı.
- **Gömülü yazı tipi:** `FontSans` (Atkinson Hyperlegible Next) projede gömülü değil; Segoe UI'a düşüyor.

## Eksik Token'lar

| Gereken | Şimdiki çözüm |
|---|---|
| `WindowMinWidth` | `SidebarWidth + ModalWidth + 2×Space5` = 848 hesaplanıyor |
| `WindowMinHeight` | `MinHeight="640"` sabit |
| pencere kaydı gecikmesi | `TSlow` ödünç |
| yazarak arama sıfırlama süresi | ListBox varsayılanı |
| INP / CLS bütçesi | yok; ölçülmedi |
| harita tür renkleri | NeonBlue, PinkText (film), PurpleText, Success, Warning, BorderDecorative ödünç |
| günlük satır aralığı | `RowGap` ödünç |
| `ToastInset` Thickness | token Double; Margin code-behind'da `new Thickness(ToastInset)` |

## Theme Ve Platform Notları

- **`SetRenderScaling` yok:** `teknesyum-ui/denetim/KontrastTests.cs` Avalonia 11.3.20'de olmayan bir API çağırıyor. `tests/DustyBytes.Tests/HeadlessScalingShim.cs` yansımayla ölçeği yazıyor; alan bulunamazsa sessizce 1'de kalır. 125/150 PNG'ler büyüyor, yani çalışıyor.
- **RenderTransform animatörü:** Theme'deki `Window.anim Panel.appbg` keyframe'i yerleşik animatör internal olduğu için çöküyordu. `TransformAnimator.Register()` `App.Initialize`'da çağrılıyor.
- **Kısayol deposu gevşek:** Başlat Menüsü kökleri yalnız `.lnk` kalıntıları için ayrı bir `QuarantineStore`'dan çıkarıldı; diğer silme yolları tam korumalı listeden geçer.
- **İptal / UAC sınırı:** UAC istemi açıkken iptal edilemez; worker açıldıktan sonra iptal geçerli.
- **Tarama kökü:** sistem sürücüsü (`Path.GetPathRoot(SystemDirectory)`); sürücü seçimi yok.
- **Yükseltilmiş worker AppData:** worker, yükselten kullanıcının `%LOCALAPPDATA%`'sına yazar; farklı yönetici hesabıyla yükseltmede defter ayrı düşer.
- **Başlatıcı kökleri:** Steam/Epic kütüphane kökleri worker'ın ProtectedList'inde yok; başlatıcı birimleri zaten başlatıcıya yönlendiriliyor, karantinaya gitmiyor.
- **Ekran 1024×768:** kayıtlı 1200×780 çalışma alanına sığdırıldı; `window.json` son çalıştırmada `0,0 1024×728`.

## Testler

`dotnet build DustyBytes.slnx`: **0 uyarı, 0 hata.**

| Proje | Sonuç |
|---|---|
| DustyBytes.Tests (bu dalga) | **35/35** — 20 VM, 13 kabuk standardı, 2 kontrast |
| DustyBytes.Units.Tests | 35/35 |
| DustyBytes.Signals.Tests | 24/24 |
| DustyBytes.Uninstall.Tests | 105/105 |
| DustyBytes.Safety.Tests | 44/44 |
| DustyBytes.Rules.Tests | 27/27 |
| DustyBytes.Scan.Tests | 17/18 |

- **Scan.Tests kırmızısı bu dalganın değil:** `FileScannerTests.InaccessibleDirectoryIsFlagged`, `ScanFixture.cs:118`'de deny ACE'li klasöre `CreateFileW` geçersiz tutamaç döndürüyor. Tarama koduna dokunulmadı; tek başına üst üste iki koşuda da kırmızı. `docs/dalga/tarama.md:127` bu testin ortamdaki yedekleme ayrıcalığına bağlı olduğunu yazıyor.
- **Kontrast:** altı ekran ayrı ayrı (`DUSTYBYTES_START_SCREEN` + `UC_ETIKET`), her biri 2/2. Çıktılar `tmp/uc/kontrast-<ekran>.txt`, görüntüler `tmp/uc/ana-{100,125,150}-<ekran>.png`.
- **Kabuk standardı:** özel çerçeve, imza konumu, MinWidth, altı ekran + roving focus, 24px hedef, watermark yok, adsız düğme yok, geçiş özellikleri, ekran başına en çok bir birincil/tehlike düğmesi, her ekran içerik çiziyor (kaldırma dahil), azaltılmış hareket, `tnum`, onay diyaloğu, cümle düzeni.

## scan.js

`node ~/.claude/plugins/cache/teknesyum/teknesyum-ui/0.6.0/scripts/scan.js .` → `245 files · 0 open · 0 fixed · 0 error(s) · 29 ignored` (çıkış 0). Ham çıktı `tmp/scan.txt`. Ignore'lardan önce: 1 hata (yanlış eşleşme), 28 uyarı (yerel metin).

## Çalıştırma Denetimi

`DUSTYBYTES_DRYRUN=1` ile exe açıldı: 8 sn sonra ayakta, başlık "DustyBytes". İkinci açılış kod 0 ile çıktı, süreç sayısı 1. `CloseMainWindow` ile kapandı, çıkış kodu 0. stderr 0 bayt (`tmp/app-err.txt`), Uygulama olay günlüğünde DustyBytes kaydı yok. Uygulamanın ayrı bir çökme günlüğü yok.
