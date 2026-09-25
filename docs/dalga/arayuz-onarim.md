# Arayüz Onarımı

Sahibin üç şikâyeti: üst çubukta düğmeler yarım çiziliyor, Öneriler geç açılıyor, yazılar küçük.
Ölçümler gerçek exe üzerinde yapıldı: prova kipi (`DUSTYBYTES_DRYRUN=1`), 96 dpi, pencere 1174×867, önbellekte 5.460 birim.
Sekme süresi, `Nav.SelectionChanged` anından ikinci `RequestAnimationFrame`'e kadar geçen zamandır; yani ilk kare çizilene kadar.
Kanca `MainWindow.axaml.cs:74-85` içinde ve yalnız `DUSTYBYTES_PERF_LOG` tanımlıyken çalışır.

## Bitiş Koşulları

- `dotnet build`: 0 uyarı, 0 hata.
- `dotnet test tests/DustyBytes.Tests`: 68/68 yeşil. İkisi bu işte eklendi: 3 durumlu bir teori ve 1 olgu.
- `scan.js`: 250 dosya, 0 open, 0 error, 29 ignored (`.claude/teknesyum-ui.json` içindeki ignore girdileri).

## Ölçüm Önce → Sonra

Her sekme için 15 adımlık aynı gezinme sırası kullanıldı. Değerler ms cinsinden.

| Sekme | Önce | Sonra |
|---|---|---|
| Öneriler | 13.250 – 23.502 (ortanca ≈ 16.300) | 10 – 43 (ara koşuda açılış anında 66) |
| Genel bakış | 26 – 2.574 | 9 – 12 (açılıştaki ilk geçiş 323 – 424) |
| Harita | 5 – 155 | 14 – 16 |
| Programlar | 7 – 47 | 14 – 42 |
| Temizlik | 3 – 78 | 3 – 36 |
| Karantina | 2 – 24 | 11 – 13 |

Ham kayıtlar `tmp/ui-onarim/` altında: `perf-once.txt`, `perf-sonra.txt` ve `perf-sayim.txt` (kart sayısı sütunlu).
Çekimler `once-*.png` / `sonra-*.png` adlarıyla aynı yerde.

Üst çubuk durumları başsız testte `:pointerover`, `:pressed` ve `:focus-visible` sınıflarıyla çizildi:
- düzeltmeden önce: `ustcubuk-once/`,
- düzeltmeden sonra: `ustcubuk-sonra/`,
- 2× büyütülmüş karşılaştırma: `ustcubuk-karsilastirma.png` (üstteki beş satır önce, alttaki beş satır sonra).

## Şikâyet 1 — Üst Çubuk Yarım Çiziliyor

**Kök neden (ölçüldü):** Hover sırasında `scale(1.02)`, düğmenin şablonundaki `Border#bd`'ye uygulanıyor. Ama `Button` kendi sınırlarında kırpıyor (`ClipToBounds` etkin). Büyüyen kenar düğmenin kendi kutusuna sığmıyor, sağ ve sol çizgiler kesiliyor.

İmzada ikinci bir kırpma daha var: `SignatureView` (UserControl) da kırpıyor.

Test çıktısından örnek: `SupportChip:pointerover Border: 864.16, 5.73, 85.68, 27.54 dışarı taşıyor, kırpma 865, 6, 84, 27 [Button#SupportChip, SignatureView#Signature]`.

Aynı hata üç yerde vardı: Güncelleme rozeti, Destek ve Teknesyum.
- Yükseklik 40 ile ilgisi yok.
- 24 px boşluk kuralıyla ilgisi yok; bu öğelerde parıltı yok.
- Margin ile ilgisi yok.

**Ne değişti:**
- `Assets/AppStyles.axaml:451-453`: bütün `Button`'lar için `ClipToBounds=False`. Ölçek ve parıltı artık düğmenin kutusunda kesilmiyor.
- `Views/SignatureView.axaml:4`: `ClipToBounds="False"`.
- Aynı taramada iki yerde daha parıltının pencere kenarında kesildiği yakalandı: Temizlik'teki `CleanButton` ve Kaldırma'daki `UninstallRun`. İkisinin de altında yalnız 12 px vardı, parıltı 20 px. Düzeltme için beş alt eylem çubuğunun iç boşluğu 24,12'den `PanelPadding`'e (24) çıkarıldı:
  - `CleanupView.axaml:14`
  - `OffersView.axaml:40`
  - `ProgramsView.axaml:20`
  - `QuarantineView.axaml:24`
  - `UninstallView.axaml:21`
- Kalıcı iki test, `tests/DustyBytes.Tests/KabukStandardiTests.cs:276-342`. İkisi de her görseli dönüşümle ve BoxShadow şişirmesiyle hesaplayıp kırpma dikdörtgenine sığıp sığmadığına bakar:
  - `TitleBar_States_Draw_Inside_Their_Clip`: üst çubuktaki her düğme × hover/basılı/odak.
  - `Every_Button_State_Draws_Whole_Glow_And_Border`: her ekrandaki her düğme × 4 durum.
- Üst çubuğa logo (`IconSize3`, 22) ve iki renkli ad (H3, "Dusty" NeonBlue + "Bytes" PinkText) eklendi: `MainWindow.axaml:32-37`. `ui.md` bunu istiyordu, eski hâlde tek renkli bir etiket vardı. Window `Icon` niteliğine dokunulmadı.

**Şablonla karşılaştırma:** `scaffold.js ustcubuk` yalnız React (`TitleBar.tsx` + `titlebar.css`) kopyalıyor. Avalonia için üst çubuk şablonu yok, dönülecek bir şablon da olmadı.

`SignatureView`, eklentideki `assets/Signature.axaml` ile birebir aynıydı, yani hata şablondan geliyor. VidShrink bu sorunu farklı çözmüş: üst çubukta ölçek kullanmıyor, kenar rengini değiştiriyor.

**Açık kalan:** Pencere düğmelerinde (`WindowButton`) odak hâli yok çünkü `Focusable=False`. Windows başlık düğmeleri de sekme durağı değildir; bilinçli olarak bırakıldı.

## Şikâyet 2 — Öneriler Geç Açılıyor

**Kök neden (ölçüldü):** Öneriler'de 5.460 kart vardı ve bunlar sanallaştırılmamış `ItemsControl` + `StackPanel` içindeydi. `DataTemplate` her gezinmede yeni bir `OffersView` kuruyor ve her seferinde 5.460 kartın görsel ağacını baştan kuruyordu. Buna ek yükler de biniyordu:
- `Grid.IsSharedSizeScope`, 5.460 kart için ortak sütun ölçümü yapıyordu.
- Her konteynere `Flip` kompozisyon animasyonu bağlanıyordu.
- `Apply`'daki fark alma O(n²) idi: `visible.Contains`, `Cards.IndexOf` ve tek tek `Insert`, yani 5.460 ayrı `CollectionChanged`.

`Load`, her `SnapshotChanged` olayında UI iş parçacığında çalışıyordu; sekme görünmezken de. Genel bakış'taki 2,5 s'lik sıçramaların ana şüphelisi bu: sonra ölçümünde kayboldular.

**Ne değişti:**
- `ViewModels/OffersViewModel.cs:18-29`: `BulkCollection<T>.ReplaceAll`. Tek bir `Reset` olayı atar.
- `ViewModels/OffersViewModel.cs:105-112`: `Ready` (Task), `IsLoading` ve `LoadingText` eklendi.
- `ViewModels/OffersViewModel.cs:134-192`:
  - Sekme görünmezken anlık görüntü değişirse yalnız "bayat" işareti konur.
  - `OnNavigatedTo` ya da görünürken gelen değişiklik `RefreshAsync`'i başlatır. Sıralama ve kart kurma `Task.Run` içinde yapılır, sonuç tek bir `Reset` ile bağlanır.
  - Eski yanıt yeni yanıtı ezmesin diye bir sayaç var. Seçim korunur.
- `UnitCard` seçimi kurucuda alanla verir. Arka planda olay tetiklenmez.
- `Views/OffersView.axaml:103-146`:
  - `VirtualizingStackPanel` kullanılıyor.
  - `SharedSizeScope` ve `Flip` kaldırıldı.
  - Satır aralığı `RowGap` ile sağlanıyor.
  - Harici birimlerde onay kutusu yer tutucu (`CheckBox.placeholder`), böylece ad sütunu hizalı kalır.
- `Views/OffersView.axaml:71-101`: iskelet. Kart düzeninde üç sabit çubuk ve üstte "N birim sıralanıyor" yazısı. Nabız animasyonu yok (ui-duzeni: "adım ya da öğe nabzı yok").
- `Controls/Flip.cs` artık hiçbir yerde kullanılmıyor, `trash/Flip.cs`'e taşındı.
- Testler: `ViewModelTests.cs:136,156,181,195` artık `await vm.Offers.Ready` bekliyor, çünkü yükleme bilinçli olarak eşzamansız.

**Dürüst not:** ui-duzeni süzgeç değişiminde FLIP ister. Sanallaştırılmış listede konteynerler geri dönüştürüldüğü için FLIP kaydırmada yanlış kayma üretir, bu yüzden kaldırıldı. Süzgeç değişimi şu an anlık; kart geçişinde hareket yok.

## Şikâyet 3 — Akıcılık

- Hover ve basılı geçişleri zaten `TInstant` (90 ms) ile yapılıyordu. Değiştirilmedi.
- Sayfa geçişi `CrossFade(TBase)` ile yapılıyor.
- Asıl akıcılık kaybı Öneriler'in 16 s'lik UI kilidiydi. Yukarıdaki düzeltmeyle ilk kare her sekmede 43 ms'nin altında (açılış anı hariç).
- UI iş parçacığı taraması: `.Result`, `.Wait()` ve büyük dosya okuması yok.
- Kalan senkron IO küçük JSON'lar: `WindowStateStore` (`TSlow` ile geciktirilmiş) ve `Ledger`. Bırakıldı.
- `MapViewModel.Load` hâlâ her `SnapshotChanged`'de senkron çalışıyor. Ölçümde sorun çıkmadı (Harita ≤ 16 ms). Öneriler'deki tembel yükleme deseni gerekirse ona da uygulanabilir.

## Şikâyet 4 — Yazı Boyu Ve Okunurluk

**Kök nedenler:**
- `FontSans` "Atkinson Hyperlegible Next, Segoe UI" adını veriyordu ama font gömülü değildi. Çoğu makinede Segoe UI ya da Inter'e düşüyordu. Şablon yorumu gömme yapıldığını iddia ediyor, yapılmıyor.
- Window düzeyinde yazı boyu verilmemişti. Temasız her `TextBlock` Avalonia varsayılanı 14'e düşüyordu: güncelleme rozeti metni, liste metinleri, ekranlardaki serbest yazılar.
- `PrimaryButton` şablonunda `FontSize` setter'ı yoktu, birincil ve tehlike düğmeleri 14 çiziliyordu. Token'a göre düğme yazısı fs-2 16 olmalı.
- Süzgeç çipleri 14'tü, düğme yazısı olarak 16 olmalı.
- Öneri kartlarında kullanım ve gerekçe satırları `Hint` (14) ile yazılıydı. Bunlar ipucu değil içerik.

**Ne değişti:**
- `Assets/Fonts/` altına Atkinson Hyperlegible Next Regular, SemiBold ve Bold ile `OFL.txt` eklendi (kaynak: VidShrink).
- `Theme.axaml:132`: `FontSans` artık `avares://DustyBytes/Assets/Fonts#Atkinson Hyperlegible Next, Segoe UI`.
- `docs/licenses.md:28`: OFL-1.1 satırı eklendi.
- `MainWindow.axaml:13`: `FontFamily=FontSans`, `FontSize=FontSize2` (16).
- `Theme.axaml:352`: `PrimaryButton` için `FontSize2`.
- `AppStyles.axaml:246-248`: `ChipItem` `FontSize2`; `MinHeight` `TargetMin`; iç boşluk 12,4. Uydurulmuş 32 kaldırıldı.
- `OffersView.axaml:129-130`: kullanım ve gerekçe satırları `Body` (16) ve satır kırmalı. Yol satırı mono `Hint` olarak kaldı.

Yazı ölçeği token dosyasıyla birebir: fs-1 14 (etiket/ipucu, taban), fs-2 16, fs-3 20, fs-4 24, fs-5 30. Yeni sayı uydurulmadı.

## Eksik Token Listesi

ui-duzeni bu token'lara atıf yapıyor ama `theme.tokens.json` ve Theme.axaml'da yoklar:
- `--tk-inp-budget`
- `--tk-cls-budget`
- `--tk-typeahead-reset`
- `--tk-window-min-width`
- `--tk-state-save-throttle`

Kodda token karşılığı olmadığı için literal kalan değerler:
- **Başlık çubuğu pencere düğmesi genişliği:** 46 (`AppStyles.axaml` `WindowButton`). Windows standardı, ama token yok.
- **Signature.axaml asset'inin kendi literal'leri:** Padding 10,3; CornerRadius 6; FontSize 14; MinHeight 24. Asset'ten geldiği gibi bırakıldı.
- **Etiket ve başlık harf aralığı (tracking):** Theme.axaml'da yazılmamış.
- **İskelet çubuğu dolgusu:** `NeonBlue10` kullanıldı. Ayrı bir "skeleton" token'ı yok.

## Teknesyum-UI'de Bu Sorunları Baştan Önleyecek Eksikler (Core İçin)

1. **Avalonia üst çubuk şablonu yok.** `scaffold.js ustcubuk` yalnız React kopyalıyor, `ui.md` ise "elle yazılmaz" diyor. Her Avalonia projesi üst çubuğu elle kuruyor ve her biri farklı hata yapıyor. `templates/ustcubuk/avalonia/TitleBar.axaml` gerekli: logo, iki renkli ad, rozet yuvası, imza, pencere düğmeleri.
2. **`Signature.axaml` asset'i kırpılıyor.** Ölçek şablonun içindeki Border'a veriliyor, `Button` ve `UserControl` ise kendi sınırında kırpıyor. Asset'e `ClipToBounds=False` eklenmeli, ya da ölçek yerine kenar/renk hover'ı kullanılmalı (VidShrink yolu).
3. **Kural var, test yok.** "Hover ölçeği ya da parıltı kutudan taşıyorsa kırpılmamalı" diye yazılı bir kural yok, bunu ölçen bir test de yok. Bu projeye eklenen `Every_Button_State_Draws_Whole_Glow_And_Border` kalıbı eklentinin test şablonuna girmeli. Görsel, dönüşüm ve BoxShadow ile şişirilip kırpma dikdörtgenine sığıp sığmadığına bakılıyor. Aynı test 24 px parıltı kuralını da kendiliğinden yakaladı.
4. **Theme.axaml'daki `PrimaryButton`'da `FontSize` yok.** Düğme yazısı 14'e düşüyor. Asset'te düzeltilmeli.
5. **Font gömme yapılmıyor ama yorum yapıldığını söylüyor.** `setup.js`, Atkinson'ın üç ağırlığını ve `OFL.txt`'yi `Assets/Fonts`'a kopyalamalı ve `FontSans`'ı `avares://` adresine çevirmeli. Kabuk testi de fontun gerçekten yüklendiğini doğrulamalı (`FontManager` üzerinden GlyphTypeface adı).
6. **Kök yazı boyu şablonda verilmiyor.** Pencere düzeyinde `FontSize2` + `FontSans` verilmezse temasız her yazı 14 oluyor. Ya şablon pencereye bunları koymalı, ya da temasız `TextBlock` < fs-2 durumunu ölçen bir test olmalı.
7. **`.claude/teknesyum-ui.json` token'larla çelişiyor.** `typography.scale` [10,13,14,18,24] yazıyor, token ölçeği ise [14,16,20,24,30]. Bu dosyayı okuyan araç yanlış ölçek görür. Kurulumda token'dan üretilmeli.
8. **Sanallaştırma kuralı ve testi yok.** ui-duzeni'de "N öğeden büyük liste sanallaştırılır" gibi bir kural yok. Kabuk testleri de "her ekranda liste sanallaştırılmış mı / görsel ağaç kaç öğe" diye bakmıyor. Bu projedeki 16 s'lik açılış bu açıktan geçti.
9. **Sekme geçiş süresinin testi yok.** `--tk-inp-budget` token'ı da yok. Bütçe token olarak tanımlanmalı. Başsız testte her sekme geçişi RAF'a kadar ölçülüp bütçeyle karşılaştırılmalı.
10. **FLIP ile sanallaştırma çelişiyor.** ui-duzeni ikisini birlikte istiyor ama Avalonia'da ikisi birlikte doğru çalışmıyor. Kural "sanal listede yalnız ekrandaki konteynerlerde, yalnız süzgeç değişiminde" diye inceltilmeli ya da bir istisna yazılmalı.

## Kapsam Dışı Kalan (Dokunulmaz Dosyalar)

- **Genel bakış'ın açılıştaki ilk geçişi 323 – 424 ms.** Sebebi önbellekteki taramanın yüklenmesi (`SessionState` / `OverviewViewModel`). Bu dosyalar sahipte olduğu için dokunulmadı.
- **Genel bakış'taki ilerleme panelinde uzun yollar kırpılıyor.** Panel `TaskProgressView.axaml` içinde, sahipte olduğu için dokunulmadı.
