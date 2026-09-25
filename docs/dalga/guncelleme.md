# Güncelleme Rozeti Ve Yayın Akışı

İki adımlı güncelleme rozeti (sarı → indir, yeşil → kur) ve `v*` etiketinden GitHub sürümü üreten iş akışı. Gerçek indirme ve kurulum yapılmadı; testler sahte `HttpMessageHandler` ve geçici klasörle çalıştı.

## Dosyalar

| Dosya | Ne |
|---|---|
| `src/DustyBytes.App/Services/UpdateService.cs` | GitHub sorgusu, sürüm karşılaştırma, düşük öncelikli indirme, SHA-256, zip açma, `apply.ps1`, `.old` temizliği |
| `src/DustyBytes.App/Services/UpdateJson.cs` | `GitHubRelease`, `GitHubAsset`, kaynak üreticili `UpdateJson` bağlamı |
| `src/DustyBytes.App/ViewModels/UpdateViewModel.cs` | `UpdateState` durum makinesi, 6 saatlik döngü, `ActCommand` |
| `src/DustyBytes.App/ViewModels/MainViewModel.cs` | `Update` özelliği, `IsBusy` (çalışan `TaskProgressViewModel` sayısı), kurucuya isteğe bağlı `UpdateService` |
| `src/DustyBytes.App/App.axaml.cs` | açılışta `.old` temizliği + `Update.Start()`, çıkışta `Stop()`, `Exit` = masaüstü ömrünü kapat |
| `src/DustyBytes.App/Views/MainWindow.axaml` | `UpdateBadge` düğmesi, üst çubukta imzanın solunda (yeni sütun 3) |
| `src/DustyBytes.App/Assets/AppStyles.axaml` | `BadgeButton` teması, `.ready` sınıfı yeşil |
| `tests/DustyBytes.Tests/UpdateTests.cs` | `FakeHttp`, `UpdateFixture`, 26 test (21 mantık + 5 headless) |
| `tests/DustyBytes.Tests/FakeBackend.cs` | `UC_ROZET=sari|iniyor|yesil` ile kontrast koşusunda rozet durumu |
| `.github/workflows/release.yml` | etiket → test → publish → zip + sha256 → `gh release create` |
| `.claude/teknesyum-ui.json` | `pair-contrast` ignore satırı 297 → 352 (tema eklenince kaydı) |
| `README.md`, `README.tr.md` | "The Program Shows What It Does" / "Program Ne Yaptığını Gösterir" altına rozet maddesi |

## Akış

1. **Sorgu.** Açılışta ve 6 saatte bir `releases/latest` okunur (`User-Agent: DustyBytes/<sürüm>`). 404, ağ hatası, bozuk JSON, taslak/ön sürüm, zip varlığı yok ya da sürüm yeni değil → rozet gizli, hata yazılmaz.
2. **Sürüm.** `tag_name` `v` öneki ve `-`/`+` eki atılarak `X.Y.Z`'ye çevrilir; derleme sürümü giriş derlemesinin `AssemblyName.Version`'ı (Directory.Build.props `Version`). Karşılaştırma üç parça üzerinden.
3. **Sarı.** "Güncelleme" + sarı nokta. İndirme kendiliğinden başlamaz; tıklayınca başlar.
4. **İniyor.** Rozet "İniyor %42". İndirme ayrı `Thread`'de, `ThreadPriority.BelowNormal`, 64 KB parça; hız sınırı normalde 4 MB/sn, tarama/silme/kaldırma sürerken (`MainViewModel.IsBusy`) 256 KB/sn. Dosya `%LOCALAPPDATA%\DustyBytes\update\<sürüm>\DustyBytes-win-x64.zip.part`'a iner.
5. **Doğrulama.** Beklenen SHA-256 önce sürüm notundan (64 hex), yoksa `DustyBytes-win-x64.zip.sha256` varlığından. Uyuşmazsa `.part` silinir, rozet sarıda kalır, hata bildirimi çıkar. Değer hiç yoksa da yeşile geçmez.
6. **Yeşil.** "Güncelleme" + yeşil nokta. Tıklayınca mevcut onay diyaloğu (`ConfirmAsync`, tehlikesiz birincil): başlık "Güncelleme kurulsun mu", metin "… Program kapanıp yeni sürümle açılacak …", düğme "Kur ve yeniden başlat". Vazgeç → yeşilde kalır.
7. **Kuruluyor.** Zip `update\<sürüm>\app` altına açılır (tek üst klasörlü zip de desteklenir), `apply.ps1` yazılır, `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File apply.ps1 -ProcessId … -Source … -Target … -Exe …` başlatılır, uygulama kapanır. Betik süreç bitene dek bekler (en çok 120 sn), her dosyayı `.old`'a çevirip yenisini koyar (kilitliyse 250 ms aralıkla 120 deneme, olmazsa `.old` geri alınır), `apply.log` yazar, uygulamayı yeniden açar. Sonraki açılışta karşılığı olan `.old` dosyaları silinir.
8. **Prova.** `DUSTYBYTES_DRYRUN=1` ya da `DUSTYBYTES_FAKE_UPDATE=9.9.9` iken ağ kullanılmaz: sahte sürüm sarı gösterilir, indirme %0→%100 taklit edilir, yeşile geçer, kurulum onayından sonra "Prova kipi: kurulum yapılmadı, program açık kalıyor." bildirimiyle yeşile döner. `FAKE_UPDATE` tek başına da taklide zorlar (sahte sürümün indirilecek dosyası yok).

Gösterim komutu (PowerShell 5.1, proje kökünden):

```powershell
$env:DUSTYBYTES_DRYRUN="1"; $env:DUSTYBYTES_FAKE_UPDATE="9.9.9"; dotnet run --project src/DustyBytes.App
```

## Yayın İş Akışı

`.github/workflows/release.yml`: `v*` etiketi → `windows-latest`, .NET 10.x → etiketten sürüm (`vX.Y.Z` değilse durur) → `dotnet test DustyBytes.slnx -c Release` → `dotnet publish src/DustyBytes.App -c Release -r win-x64 --self-contained -o publish` → `DustyBytes-win-x64.zip` + `.sha256` (`<hash>  <ad>`) → `gh release create --verify-tag` (ekli `-` varsa `--prerelease`). Sürüm notu SHA-256'yı ve "not code-signed" uyarısını taşır.

Test ve publish'e `-p:Version=<etiket>` verilir: aksi halde yayımlanan exe kendini hep `0.1.0` sanır ve rozet her açılışta yine sarı yanar.

## Testler

`dotnet build DustyBytes.slnx --no-incremental`: **0 uyarı, 0 hata.**

| Proje | Sonuç |
|---|---|
| DustyBytes.Tests | **64/64** (38 mevcut + 26 güncelleme) |
| DustyBytes.Units.Tests | 39/39 |
| DustyBytes.Signals.Tests | 24/24 |
| DustyBytes.Uninstall.Tests | 105/105 |
| DustyBytes.Safety.Tests | 44/44 |
| DustyBytes.Rules.Tests | 27/27 |
| DustyBytes.Scan.Tests | 18/18 |

Güncelleme testleri: sürüm karşılaştırma (7 durum + 3 bozuk etiket), sürüm notundan SHA bulma, 404 → gizli, ağ hatası → gizli, eski/eşit sürüm → gizli, SHA uyuşmazlığı → sarıda kalır ve dosya bırakmaz, SHA yok → sarıda kalır, `.sha256` varlığından doğrulama, tam durum makinesi `None → Available → Downloading → Ready → Installing` (betik argümanları, `-WindowStyle Hidden`, açılan dosyalar, `Exit` çağrısı), reddedilen kurulum yeşilde kalır, prova kipi ağsız ve dosyasız, indirme iş parçacığı `BelowNormal` ve meşgulken yavaş (256 KB ≥ 350 ms), `.old` temizliği yalnız karşılığı olanı siler.

Headless: rozet varsayılan gizli; üç halde görünür, metin doğru, nokta rengi `Warning`/`Success` token'ı, ≥24 px, imzanın solunda ve pencerenin sağ yarısında. Çekimler `tmp/uc/rozet-sari.png`, `rozet-iniyor.png`, `rozet-yesil.png` (üst çubuk kırpıntıları `rozet-*-kirp.png`).

Kontrast: `UC_ROZET` ile üç halde ayrı koşu, her biri 2/2. Rozet yazısı sarı 11.94:1, yeşil 10.37:1. Çıktılar `tmp/uc/kontrast-rozet-*.txt`, `tmp/uc/ana-{100,125,150}-rozet-*.png`.

`apply.ps1` elle denendi (`tmp/apply-sandbox`): `DustyBytes.exe` → yeni + `.old`, `rules/protected.json` eklendi, dokunulmayan dosya kaldı, günlük "done, failed 0".

scan.js: `246 files · 0 open · 0 fixed · 0 error(s) · 29 ignored` (`tmp/scan-guncelleme.txt`).

## Token Eksikleri

| Gereken | Şimdiki çözüm |
|---|---|
| durum noktası boyu | `Space2` (8) ödünç |
| `Success50` (yeşil kenar yarı saydam) | sarıda `Warning50`, yeşilde tam `Success` kenar |
| rozet iç boşluğu | `InputPadding` (12,0) ödünç |
| güncelleme sorgu aralığı, indirme hız sınırları | kodda sabit: 6 sa, 4 MB/sn, 256 KB/sn |

## Notlar Ve Açık Kalanlar

- **Depo özel.** API isteği kimliksiz; depo açılana dek her sorgu 404 döner ve rozet hiç görünmez. Bu beklenen durum.
- **İmzasız.** Sürüm notunda yazıyor; SmartScreen ilk açılışta uyarabilir.
- **Yükseltilmiş worker.** Kurulum ana süreç kapanınca başlar; worker bir süre daha açıksa exe'nin yeniden adlandırılması Windows'ta yine çalışır, kopya kilitlenirse betik 30 sn dener.
- **CI'da Scan.Tests.** `FileScannerTests.InaccessibleDirectoryIsFlagged` önceki dalgada ortama bağlı kırmızıydı; bu makinede şimdi yeşil. Runner'da kırmızı çıkarsa sürüm üretilmez.
- **İlerleme çubuğu yok.** Rozet yalnız metin gösteriyor ("İniyor %42"); standarttaki tavan kuralı panel içindir, rozete çubuk eklenmedi.
