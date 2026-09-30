# Denetim 2: En Küçük Pencere (1076x679, Yazı Boyutu %125)

Kaynak: `tmp/yakala/06-en-kucuk.png`. Açılış ekranı, tarama sürüyor (%28). Yalnız görüntüye bakıldı; kaynak koda dokunulmadı. Konumlar görüntü pikseliyle verilmiştir.

## Genel Görünüm

Üst üste binme ve pencere dışına taşma yok. Başlık çubuğu, kenar çubuğu, sayfa başlığı ve tarama kartı düzgün oturuyor. İlerleme çubuğu dolgusu (%28, yaklaşık 362 ile 545 px arası) yüzdeyle tutarlı. Birincil eylem düğmesi "Otomatik temizle" görünüyor, kontrastı iyi. Sorunlar ağırlıkla kartın içeriğinde, hizalamada ve metin anlaşılırlığında.

## Bulgular

### 1. Yüksek: "Sürücüler" kartı, "C:" etiketi (x≈485, y≈671)

- Açıklama: "C:" etiketi kart başlığının (x≈362) yaklaşık 120 px sağında ve alt kenara yapışık duruyor; kartın kendi sol hizasına oturmuyor, yarım kesik görünüyor.
- Öneri: Etiketi sürücü satırının sol hizasına (x≈362) al. Bu boyutta kartın ilk satırı tam görünsün ya da sayfa kaydırılabildiği belli olsun. Gerçek sayfayı kaydırarak "C:" etiketinin sürücü çubuğuyla ilişkisini de doğrula.

### 2. Orta: Sayfa altında kesilen içerik, kaydırma ipucu yok (y≈575 ile 679 arası)

- Açıklama: "Sürücüler" kartının yalnız başlığı ve bir sıra görünüyor, kaydırma çubuğu ya da solma efekti yok; kullanıcı alt içeriğin varlığını fark etmeyebilir.
- Öneri: Kaydırma çubuğunu görünür kıl ya da alt kenarda hafif bir solma/gölge ekle. Tarama sürerken tarama kartını daha kısa tut ki sürücüler kartı daha çok görünsün.

### 3. Orta: Üst eylem satırı, düğme kimliği belirsiz (y≈136)

- Açıklama: "Hızlı tarama (yönetici)", "Baştan tara", "Yenile" düz gri metin gibi duruyor; tıklanabilir mi, tarama sürerken pasif mi oldukları anlaşılmıyor. Yanlarındaki dolu mavi "Otomatik temizle" ile görsel dil çok farklı.
- Öneri: İkincil düğmelere ince çerçeve ya da zemin ver, pasifse belirgin soluklaştır. Tarama sürerken geçerli olmayan eylemleri gerçekten pasif göster.

### 4. Orta: "Otomatik temizle" birincil eylem olarak tarama bitmeden aktif duruyor (x≈815-1000, y≈136)

- Açıklama: Silme içeren bir eylem tarama %28'deyken en vurgulu düğme ve tıklanabilir görünüyor; ne yapacağı ("otomatik") belirsiz, ve sonuçları henüz hazır değil.
- Öneri: Tarama bitene kadar pasif yap ya da "Taramayı bitir" gibi o anki asıl eyleme çevir. Etiketi ne temizleyeceğini söyleyecek şekilde netleştir (örneğin "Güvenli olanları temizle").

### 5. Orta: Durum rozeti ile düğme karışıklığı, terim kalabalığı (x≈510-637, y≈85)

- Açıklama: "Tazeleniyor" rozeti çerçeveli, düğme gibi görünüyor ama durum bilgisi; altındaki kartta "Taranıyor", satırda "Yenile" var. Tazeleme, tarama ve yenileme aynı anda üç ayrı söz olarak duruyor.
- Öneri: Rozeti düğme çerçevesinden ayır (dolgusuz, küçük nokta ya da etiket) ve tek terim kullan; tarama sürerken rozeti gizle ya da "Taranıyor" ile birleştir.

### 6. Orta: Tarama yol listesi, kesik ve gürültülü (x≈362-1006, y≈310-500)

- Açıklama: Sekiz satır parlak beyaz tek aralıklı yazı sürekli akıyor, hepsi soldan "…" ile kesilmiş, sağ uçta ise sert kırpılıyor; anlamlı bir yol okunamıyor ve asıl içeriği (ilerleme) bastırıyor.
- Öneri: Tek satıra indir (yalnız şu anki klasör), soluk ikincil renkte, ortadan kısalt (`C:\…\son-klasör`). Sekiz satır gerekiyorsa daha soluk ve küçük yaz.

### 7. Düşük: Kart içi sağ kenar hizası (x≈1001 ile 1014)

- Açıklama: "%28" ve "İptal et" sağ kenarı (≈1001) ilerleme çubuğunun sağ ucuyla (≈1014) ve kartın iç sağ kenarıyla hizalanmıyor; "Otomatik temizle" de aynı 1000 civarında bitiyor.
- Öneri: Tüm sağa dayalı öğeleri kartın aynı iç kenarına hizala (düğme iç dolgusunu hizaya dahil et).

### 8. Düşük: Sol hiza tutarsızlığı

- Açıklama: Sayfa başlığı ve kartlar x≈331'de başlarken üst eylem satırının ilk düğme yazısı x≈344'te; kenar çubuğunda "Yazı boyutu" x≈31, menü simgeleri x≈47, "DustyBytes" logosu x≈17; hepsi farklı sol çizgilerde.
- Öneri: Düğme iç dolgusunu hizaya kat (ya da satırı 13 px sola al), kenar çubuğundaki öğeleri tek bir sol çizgiye topla.

### 9. Düşük: "İptal et" zayıf görünüyor (x≈933-1001, y≈232)

- Açıklama: Bir tarama sırasında tek iptal yolu küçük mor metin; düğme gibi durmuyor, yanındaki pembe "%28" ile aynı satırda renk kalabalığı yaratıyor (mavi, pembe, mor üç vurgu).
- Öneri: İptali ikincil çerçeveli düğme yap, yüzdeyi ana metin renginde bırak.

### 10. Düşük: "Yazı boyutu" denetimi (x≈31-272, y≈641)

- Açıklama: "−" ve "+" çok küçük, mor ve düşük kontrastlı; tıklama alanı dar görünüyor.
- Öneri: Düğme alanını en az 32x32 px yap, kenarlık ya da zemin ekle.

### 11. Düşük: Başlık çubuğu, "Teknesyum" ve "Destek Ol" (x≈711-940, y≈21)

- Açıklama: İkisi de renkli düz metin; bağlantı mı düğme mi belli değil, "Destek Ol" mor tonda koyu zeminde görece düşük kontrastlı. Başlık çubuğunun alt çizgisi yalnız kenar çubuğu genişliğinde (0-300 px), içerik alanında yok.
- Öneri: Bağlantılara üzerine gelince alt çizgi ya da zemin ver, kontrastı artır; alt çizgiyi tüm genişliğe uzat ya da hiç kullanma.

## Özet Tablo

| Önem | Adet |
|---|---|
| Yüksek | 1 |
| Orta | 5 |
| Düşük | 5 |
