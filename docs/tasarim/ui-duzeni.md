# UI Düzeni

Teknesyum-UI eklentisinin `SKILL.md` gövdesinden çıkarıldı (depo arşivlendi). Sayısal
değerler burada yazmaz: renk, yarıçap, süre, ölçü ve boşluk projenin üretilmiş
`theme.tokens.json` / `theme.css` (`--tk-*`) dosyasından okunur; WPF-Avalonia tarafında
`Theme.xaml` / `Theme.axaml`. Token yoksa bana sorulur, uydurulmaz.

## Renk
- Yalnız koyu tema. `prefers-color-scheme: light` yok sayılır; açık tema demek on bir rengi
  yeniden ölçmek demektir.
- Dolgulu neon düğme siyah yazı taşır. Neon üstünde beyaz 1.38:1'dir, kullanılmaz.
- Orta gri yok. Gösterilmeye değer metin beyazdır, değilse silinir.
- Sıradüzen boyutla kurulur, kalınlıkla ya da parlaklıkla değil: ölçekte bir basamak yukarı.
- Pembe ile mor arası ΔE 5.8; tek ayırt edici olamaz. Biri maviye çevrilir ya da ikinci bir
  taşıyıcı eklenir.
- `info` rengi yoktur. Nötr bildirim varsayılan kenar + beyaz yazıdır.
- `warning` yazı, kenar ve simgedir; dolgu ya da düğme değildir.
- Parıltı kutuya konur, yazıya hiç. Tek istisna kahraman sayı.
- Dışa parıltı 24 px boş alan ister, yoksa `/50` kenar kullanılır.
- Liste satırı, tablo satırı, hücre ya da `.map()` / `ItemsControl` çıktısı parlamaz;
  kapsayıcı parlar. Bedel kaydırmada yeniden çizim, eleman sayısı değil.
- `forced-colors` altında arayüz sistem paletine bırakılır, neon savunulmaz.

## Okunurluk
- Her yazı ve simge gerçekten üstünde durduğu zemine karşı ölçülür, siyaha değil. Saydam
  dolgu altındaki zemine bindirilir, çıkan tek renge karşı ölçülür; degrade ya da görüntü
  zeminde en kötü nokta sayılır.
- Dolgulu yüzeyin (düğme, rozet, çip, seçili satır) yazısı ve simgesi yalnız o dolgunun `on`
  eşi token'ından gelir. `--tk-text` ya da `--tk-text-label` dolgu üstüne yazılmaz.
- Dolgu ile yazı aynı ton ailesinden olamaz: mavi üstüne mavi, mor üstüne mor, pembe üstüne
  pembe yok. Saydam dolgu da ailesini taşır; açık mavi dolguda koyu mavi yazı hatadır.
- Eşik mevcut 7:1 metin eşiğidir; büyük metin ayrı, gevşek eşik almaz. Kenar ve simge
  temadaki metin dışı eşiğe tabidir.
- Her durum ayrı ölçülür: dinlenik, hover, basılı, seçili, odak, edilgen. Edilgenin 7:1
  muafiyeti ve işaret zorunluluğu kalır; oranı yine ölçülüp rapora yazılır.
- Ölçüm kanıtı canlı denetimden gelir (`ui-denetim.md`). Durağan tarama yalnız palet dışı
  rengi siyaha karşı ölçer; tek başına kanıt sayılmaz.

## Hareket
- Hareket temeldir, süs değil: panel, sekme, liste değişimi, bildirim, değer değişimi ve
  yükleme canlanır. "Gerekli görünmedi" gerekçe sayılmaz.
- Odak halkası 0 ms'de belirir — tek istisna.
- Yükleme, gelecek düzeni tutan iskelet gösterir; döner çark değil.
- Pencere gizli açılır (`visible: false`), boyut kurulunca gösterilir; beyaz ilk kare ve boyut zıplaması hatadır.
- Yalnız `transform` ve `opacity` canlanır. İlerleme dolgusu `scaleX`, genişlik değil; tarama ışığı `::after` + `translateX`, `background-position` değil. `transition` içine `box-shadow` / `background` girmez.
- Sonsuz döngü yalnız ilerleme kapsamında yaşar, süresi `--tk-loading-loop-min`; adım ya da öğe nabzı yok.
- `prefers-reduced-motion` döngüleri durdurur; metin, yüzde ve durum rengi kalır.
- Dolu çubuğun üstüne yazı konmaz; yüzde çubuğun yanında durur.
- Canlı günlük alta hizalanır, en yeni satır parlak; eski satırlar yukarı akar.
- Süre role göre kademelenir: hover ve basılı geri bildirimi en kısa `--tk-t-*` basamağı,
  açılır menü orta, panel ve pencere en uzun. Basamak dışı süre yazılmaz.
- Ekrana giren öğe `--tk-e-out` ile açılır, hiçbir giriş `--tk-e-in` ile değil; `--tk-e-in`
  yalnız çıkışta. Sabit tekrarlı döngü doğrusaldır.
- Giren öğe sıfır ölçekten başlamaz: hafif küçük ve saydamdan gelir. İletişim kutusu
  ortada sabit kalır.
- Açılır menü tetikleyicisinden büyür; `transform-origin` tetikleyicinin konumudur.
- Tekrar tetiklenen öğe (bildirim, anahtar, sürüklenen panel) `transition` ile canlanır,
  `@keyframes` ile değil; kesilirse o anki konumundan döner, baştan sıçramaz.
- Liste ekle/çıkar/sırala'da kalan öğeler yeni yerine kayar (FLIP), sıçramaz; çıkan öğe
  akıştan hemen düşer.
- `transition: all` yazılmaz; canlanan özellik adıyla sayılır.
- `will-change` yalnız canlanmak üzere olan öğeye verilir, bitince kaldırılır; kalıcı ya da
  sayfa geneli değil.
- Avalonia'da sekme ve sayfa değişimi `PageTransition` / `CrossFade` ile verilir; elle
  `Opacity` / `Margin` animasyonu yazılmaz.

## Form ve iletişim kutusu
- Yer tutucu metin yok. Görünür etiket, altında yardım metni.
- Onay kutusu arka plana tıklamayı yok sayar, bilgi kutusu kapanır; `Esc` ikisini de kapatır.
- Geri alınabilir eylem onaylatılmaz: yapılır, sonra geri alma sunulur.
- Hata bildirimi kendi kendine kapanmaz. Diğerleri 6 sn yaşar; fare ve klavye odağı duraklatır.
- İletişim kutusu açılınca odak içeri girer, Tab dışarı çıkmaz; nasıl kapanırsa kapansın
  (Esc, düğme, dışa tıklama) odak açan denetime döner. Hedef kodda açıkça verilir.
- Açılır kutu ve menü dışa tıklamayla ve Esc ile kapanır; arka planın kaydırılması tek
  başına kapatmaz.
- Menü, liste, sekme ve araç çubuğu grubu Tab'da tek duraktır (roving tabindex); içeride ok
  tuşları gezer, Home/End uca atlar, sağ/sol ok alt menüyü açıp kapar. `tabIndex` sıfırdan
  büyük olmaz, `autoFocus` yok.
- Liste ve menüde art arda yazılan harfler birikir ve eşleşen öğeye gider; dize
  `--tk-typeahead-reset` sonra sıfırlanır.
- Combobox ve komut paleti: odak yazı alanında kalır, ok tuşları seçimi taşır
  (`aria-activedescendant`); ilk eşleşme seçili gelir, liste yazıldıkça güncellenir, sonuç
  yoksa boş durum cümlesi görünür.
- Her tıklanabilir hedef `--tk-target-min`'den küçük değildir; küçükse çevresinde eşdeğer
  boş alan kalır.
- `:hover` taşıyan her seçicinin `:focus-visible` eşi vardır; `outline: none` yalnız yerine
  görünür halka konmuşsa.

## Metin
- Tablo başlığı ve hücresi iki yönde ortalanır.
- Her görünür etiket, cümleler dahil: ilk harf büyük, gerisi küçük; bağlaçlar küçük kalır.
  Türkçe kendi harf haritasını ister (`toLocaleUpperCase('tr')`).
- Arayüz dili yapılandırmadaki `language` (varsayılan `tr`). Depo README ve teknik belge
  yine İngilizce.
- Sayı gösteren her metin (yüzde, süre, boyut, saat) sabit genişlikli rakamla dizilir
  (`tabular-nums`).

## Kapsam
- Davranış kitaplığı alınır; görsel tema kitaplığı asla — MUI, WPF UI, MahApps, HandyControl yok.
- Bileşen kopyalamadan önce lisans denetlenir; lisansı olmayan sahiplidir. Her ödünç
  `docs/licenses.md`'ye yazılır.
- Gösterişli efektler — WebGL, parçacık, kaydırma sahnesi, `gsap` — ayrı tanıtım sayfasına
  aittir, uygulamanın içine girmez.

## Masaüstü
- Sistem başlık çubuğu kaldırılır, kendi çubuğumuz çizilir; sonra kırılan her şey geri
  verilir: sürükleme, çift tıkla büyütme, Aero Snap, kenardan boyutlandırma, `Alt+F4`.
- İmza bloğu başlık çubuğunda, küçültme düğmesinin solunda durur. Altbilgide ya da
  ayarlarda değil. Dururken tam opak; üzerine gelince `scale(1.02)`, saydamlık değil.
- Başlık alanı genişletmesi pencere gösterilmeden önce kurulur; açılışta sistem başlığı
  yanıp sönmez.
- Sürükleme bölgesi başlık kabının tamamıdır; içindeki düğmeler sürüklemeden açıkça
  çıkarılır.
- Büyütme düğmesi üzerine gelince Snap Layout menüsünü açar. En küçük pencere genişliği
  `--tk-window-min-width`, Snap bölgesine sığar.
- Pencere konumu ve boyutu hazır eklentiyle saklanır (`tauri-plugin-window-state`);
  büyütülmüşken sınır değil bayrak yazılır. Kayıt taşıma olayının içinde değil,
  `--tk-state-save-throttle` ile kısılarak yapılır.
- Geri yüklenen konum ekranların birleşiminin dışındaysa ortalanmış varsayılana düşer.
- Tek örnek zorunludur: ikinci açılış mevcut pencereyi öne getirir ve argümanı ona iletir.
- Piksele oturan çizim `RenderScaling` / `scale_factor`'ü çalışma anında okur; ölçek
  sabiti yazılmaz.
- Kapatma, arka plan işi sürerken pencereyi tepsiye gizler; gerçek çıkış ayrı yoldur ve
  ayarla seçilir. Tepside sol tık pencereyi getirir, sağ tık yerel menüyü açar.

## En iyi program çalıştığını gösterir
- Arayüz iş parçacığı asla bloklanmaz; arayüz süreci alt süreçleri eşzamansız koşar.
- ~1 sn üstü iş ilerleme gösterir: çubuk kendi adımının içinde sürünür, adım adı görünür.
- Arka plan durumu — senkron, güncelleme, bağlantı — her zaman ekrandadır.
- Sonuç duyurulur: başarı kısa bildirim, başarısızlık insan cümlesi + günlük yolu.
- Kurulum ürünün parçasıdır; kurulum penceresi `guncelleme-paneli.md` ölçütüne uyar.
- Etkileşim yanıtı ölçülür ve `--tk-inp-budget` içinde kalır (web'de `onINP`); olay
  işleyicisi ana iş parçacığını uzun görevle tutmaz.
- Düzen kayması `--tk-cls-budget` içinde kalır (`onCLS`): medya boyutu önceden bilinir,
  geç gelen içerik üsttekini itmez.
- İskelet yalnız düzeni bilinen içerik içindir; düzeni bilinmeyen işte iskelet uydurulmaz,
  adım adı ve çubuk gösterilir.
- Kullanıcının başlattığı uzun iş iptal edilebilir; iptal duyurulur ve işi temiz bırakır.
  Kur penceresi `guncelleme-paneli.md` istisnasıdır.
- Boş ve hata ekranı sonraki adımı söyler ve tek birincil eylem taşır; o eylem dolu
  hâldeki yerinde durur.

## Doğrulama
Derlenmesi hiçbir şey kanıtlamaz, durağan tarama render göremez. Her aşamada uygulama bir
kez açılır, ekran görüntüsü alınır ve yakalanan pencerenin süreç yolunun bu depoya ait
olduğu doğrulanır. O geçişte şunlar gezilir: en küçük pencere boyutu, her sekme, hover /
odak / seçili / edilgen / açık açılır liste, panel alt kenarları, hareket temeli, klavye
ile dolaşım, dil değişimi. Hata ve boş ekranlar dahil. Şüpheli kenar en az 4× en-yakın-komşu
büyütmeyle kırpılıp bakılır; 1 DIP 1:1'de görünmez.
- Masaüstü uygulamada tarayıcı provası pencereyi temsil etmez: görünüm alanı pencerenin iç
  ölçüsü değildir, DPI ölçeği yoktur. Gerçek exe'nin penceresi yakalanır, %125 ve %150'de de.
- Sabit boyutlu pencerede birincil eylem kutusu pencerenin içinde ölçülür; gövdede küçülebilir
  bir alan (günlük, liste) olur, birincil düğme küçülen ilk şey olmaz.
- İki iddia, iki kanıt: "çalışıyor" (başsız test) ile "kullanılabilir" (görüntü) ayrı satırdır.
- Birden çok dosyaya dokunan düzeltme, rapordan önce her dosyada `rg` ile doğrulanır.
- Görüntüye işi yapmamış bir alt ajan bakar: kesik yazı, taşan düğme, görünmeyen birincil
  eylem, boşa duran denetim.

## Rapor
UI işi bir etki bloğuyla biter: `dosya:satır`, kural, ne değişti — ve **uygulanmayan**
kurallar, gerekçesiyle. Bunu diff'ten okuyamam.

## Öncelik
1. Proje yapılandırmasındaki `note:` alanı 2. bu dosya 3. tarayıcı bulguları
4. platform notları 5. projenin kendi mevcut biçemi.
Burada ve tarayıcıda olmayan kural standardın parçası değildir; türetilmez.
