# Görüntü İncelemesi — Yazar Olmayan Alt Ajan

## İstem (Birebir)

```
Türkçe yanıt ver. Sen bu arayüzü yazmadın; yalnız görüntülere bakarak değerlendir, kod okuma.

Klasör: C:\Users\Administrator\Desktop\Projeler\DustyBytes\docs\ui-denetim\2026-09-27b\

1. `yan-yana/*.png` (ustcubuk, dugmeler, bildirim-panel, modal-temizlik): solda sahibin Teknesyum-UI önizlemesi (standart), sağda DustyBytes uygulamasının gerçek penceresi. Soru: uygulama önizlemedeki standardı taşıyor mu? Üst çubuk yüksekliği ve düzeni, çerçevesiz çipler, düğme boyu ve dolgusu, köşe yarıçapı, düz siyah zemin (gradyan yok), renkler (mavi #6fb7ff, pembe yazı #fa8cff, mor #ac7fff/#c3a3ff), gölge yok. Her farkı somut yaz: hangi görüntü, hangi öğe, önizlemede nasıl, uygulamada nasıl.
2. Ekranlar tek tek: `genel-100-sonra.png`, `oneriler-100-sonra.png`, `harita-100-sonra.png`, `programlar-100-sonra.png`, `temizlik-100-sonra.png`, `karantina-100-sonra.png`, `guncelleme-var-100-sonra.png`, `guncelleme-iniyor-100-sonra.png`, `guncelleme-hazir-100-sonra.png`, `yukleniyor-100-sonra.png`. Soru: kullanılabilir mi? Okunmayan yazı, kırpılma, taşma, hizasızlık, anlaşılmayan durum, eksik geri bildirim.
3. Simge: `C:\Users\Administrator\Desktop\Projeler\DustyBytes\tmp\simge\onizleme.png` (yeni) ve `C:\Users\Administrator\Desktop\Projeler\DustyBytes\trash\simge\DustyBytes-eski.png` (eski). Yeni simge temanın renkleriyle uyumlu mu, küçük boyda okunuyor mu?

Çıktı: önce tek satır karar "standarda uyuyor: evet/hayır" ve "kullanılabilir: evet/hayır", sonra bulgular tablosu (görüntü | öğe | önem: yüksek/orta/küçük | ne görülüyor). En fazla 40 satır.
```

## Çıktı (Birebir)

**Karar:** Standarda uyuyor: **hayır** (kısmen uyuyor, üç belirgin sapma var). Kullanılabilir: **evet, ama Programlar ekranı boş ve güncelleme paneli içeriğin üstüne biniyor.**

Zemin düz siyah ve gradyansız. Gölge yok. Mavi, pembe ve mor doğru yerlerde; marka adı mavi+pembe olarak iki renkli.

| Görüntü | Öğe | Önem | Ne görülüyor |
|---|---|---|---|
| yan-yana/ustcubuk | Güncelleme çipi | orta | Önizlemede tek istisna çerçeveli çip: sarı ya da yeşil kenar, yazının altında ortalı çizgi. Uygulamada çerçevesiz, düz yazı. |
| yan-yana/ustcubuk | Durum çipi | orta | Önizlemede "Eşitlendi · 14:32" / "Çevrimdışı" çipi var, uygulamada yok. |
| yan-yana/ustcubuk | Sağ grup sırası | küçük | Önizlemede sıra Teknesyum → ☕ Destek Ol → teknesyum.com. Uygulamada Destek Ol → Teknesyum; fincan simgesi ve alan adı bağlantısı yok. |
| yan-yana/ustcubuk | Alt ayraç çizgisi | küçük | Önizlemede çizgi çubuğun tamamı boyunca uzanıyor. Uygulamada yalnız yan menünün üstünde; içerik alanında yok. |
| yan-yana/dugmeler, oneriler | Filtre sekmeleri (Tümü/Oyun/Film…) | yüksek | Önizlemede sekmeler çerçevesiz, seçili olan beyaz yazı ve alt çizgiyle gösteriliyor. Uygulamada her sekme kenarlıklı kutu, seçili olan dolu mavi düğme. |
| yan-yana/dugmeler | Hayalet düğme ("Yeniden tara") | küçük | Önizlemede hayalet düğmenin yazısı beyaz. Uygulamada gri yazı ve gri kenar; pasif sanılıyor. |
| yan-yana/bildirim-panel | Güncelleme paneli | yüksek | Önizlemede bildirim renkli kenarlı ve × ile kapanıyor. Uygulamada nötr kenarlı, × yok, üstelik başlık satırını ve "İptal et" / "Yeniden tara"yı örtüyor. Arkadaki yazı yarı saydam biçimde sızıyor. |
| yan-yana/modal-temizlik | Modal | orta | Sağ tarafta modal yok, yalnız Temizlik sayfası var. Karşılaştırma yapılamadı; onay penceresinin görüntüsü eksik. |
| guncelleme-iniyor/hazir | Panel ilerleme çubuğu | orta | Çubuk maviden pembeye gradyan. Tarama çubuğu düz mavi; iki çubuk tutarsız ve gradyan kuralına aykırı. |
| programlar | Liste | yüksek | Arama kutusunun altı tamamen boş. Ne "yükleniyor" ne "program bulunamadı" yazıyor; kullanıcı durumu anlayamıyor. |
| temizlik | Boyut sütunu | yüksek | Her satırda "Ölçülüyor" yazıyor, ama alt çubuk "ölçülenler en az 502 MB" diyor. Çelişkili; ölçümün bittiği anlaşılmıyor. |
| temizlik | Kaynak/lisans satırları | küçük | Her grubun altında uzun, çok küçük "bleachbit fikri (GPL…)" satırı var. Okunmuyor ve gürültü yapıyor. |
| temizlik | "Seçilenleri temizle" | orta | Prova kipinde dolu mavi, etkin görünüyor. Sonucun sahte olacağı düğmede belli değil. |
| oneriler | Oyun kartları | orta | Oyun kartlarında onay kutusu yok, başlık girintisi boşlukla kaydırılmış. Neden seçilemedikleri söylenmiyor, yalnız "Başlatıcıda aç" var. |
| oneriler | Kart satırları | küçük | "Son kullanım 23 gün önce" ile "Son oynanma 23 gün önce, 98,8 GB" aynı bilgiyi tekrarlıyor. |
| harita | Küçük dilimler | küçük | Sağ alttaki dilimler etiketsiz. "Users 560 GB" alanın yarısını kaplıyor ama rengi yok (Diğer). |
| genel, guncelleme-* | Taranıyor listesi | küçük | Yollar dar pencerede baştan "…" ile kırpılıyor (doğru). Geniş pencerede satır uzunlukları düzensiz duruyor, okunuyor. |
| yukleniyor | Kayıt satırları | küçük | "1.015.808 / 4.070.020 kayıt" türünden 9 satır üst üste akıyor. Tek satır ve yüzde yeterli, gerisi gürültü. |
| yukleniyor | "Yeniden tara" | küçük | Yükleme sürerken etkin görünüyor. |
| 100% ekranların hepsi | Yazı boyu | orta | 2576 px genişlikte gövde yazısı ve menü simgeleri çok küçük (≈11 px); açıklama satırları zor okunuyor. |
| hepsi | Prova kipi kutusu | küçük | Geniş pencerede bile "hiçbir dosya / silinmez" iki satıra bölünüyor; yan menü dar. |
| simge (yeni) | Renkler | — | Uyumlu: halka pembeden (#fa8cff) mora, nokta mavi (#6fb7ff), çerçeve mor (#ac7fff). Eskideki neon magenta, camgöbeği ve parlama gitmiş. |
| simge (yeni) | Küçük boy | orta | 48 ve 32 px'de halka ve nokta okunuyor. 24 px'de üç kare nokta haline geliyor. En küçük boy önizlemede sağdan kesik, değerlendirilemedi. |
| simge (yeni) | Halka gradyanı | küçük | Halkada pembe→mor gradyan var. Kural zemin için ise sorun değil; genel bir "gradyan yok" kuralıysa aykırı. |

Not: Üst çubuk yüksekliği ve düğme boyu piksel olarak karşılaştırılamadı. Önizleme ile uygulama görüntüleri farklı ölçeklerde alınmış; göz kararı ikisi de ince ve tek satır.

Yok
