# Danışma 004 girdi: Danışma: Kullanıcı Verimi — DustyBytes

Ajana giden metin:

---

[[danisma:004]]

# Danışma: Kullanıcı Verimi — DustyBytes

## Sahibin sözü (aynen)
"fable ve sen düşün kullanıcı verimi için neler yapılabilir ve yap araştırma yap tekrardan benzerlerinden çok daha iyi olmalıyız"

Önceki söz: "piyasadaki en iyi olacaz en pratik olacaz mükemmel olacaz artıksız hızlı kullanışlı ne ararsan bizde kullanıcı dostu olacaz"

## Ürün bugün (0.6.0, 2026-09-30)
- Windows disk temizleyici; Avalonia + .NET 10; arayüz yetkisiz, ayrı `--worker` yönetici süreci; her silme/taşıma worker'da korumalı listeden (SafetyGate) geçer.
- Bütün sabit sürücüleri tarar (MFT okuyucu, ~9 sn / 2.8 M dosya; USN imleciyle hızlı yeniden tarama). Sonuçlar akarak gelir.
- Dosyaları amacına göre "birim" yapar: oyun, program, film, dizi, geliştirici artığı, önbellek, tarayıcı önbelleği, kurulum dosyası, sistem artığı, eski indirilenler, Geri Dönüşüm Kutusu, hazırda bekletme, bulut kopyası (OneDrive), kopya dosyalar. Her birimin boyutu, son kullanım tarihi (launcher, Prefetch, UserAssist), güven düzeyi ve gerekçesi var.
- Ekranlar: Genel bakış (sürücü seçici, tarama, "Otomatik temizle"), Öneriler (kart başına tek tık; filtreler), Harita, Programlar (tekli, zorla, toplu kaldırma; kalıntıları karantinaya), Temizlik (23 kural + DISM, WU önbelleği), Karantina (7 gün sonra otomatik boşalır; geri al).
- Varsayılan karantina; kalıcı silme iki basışla ya da kategori politikasıyla. Tek tık → anında karantina, bildirimde "Geri al".
- Otomatik mod/tur: güvenli olanları sormadan siler, kişisel dosyalara sormadan dokunmaz; kalanı kart kart sorar (Kalsın / Karantinaya al / kalıcı sil).
- Silmeden yer aç: WOF saydam sıkıştırma "Küçült", OneDrive "yalnız çevrimiçi".
- Haftalık kullanıcı düzeyi görev boş alanı ölçer, azsa Windows bildirimi; Explorer sağ tık "DustyBytes ile incele"; açılan yer geçmişi.
- Yazı boyutu yakınlaştırma (%125 vb.), açılışta ekranı kaplar. Yalnız Türkçe (İngilizce ertelendi). Kod imzası yok (SmartScreen uyarısı çıkıyor).

## Kalan bilinen eksikler
- Başlangıç programları, büyük birimi başka sürücüye taşı, gölge kopya alanı, tarayıcı kapanınca temizle, paket yazılım tespiti.
- Arayüz denetimi: ikincil düğmeler pasif görünüyor; tarama sırasında akan yol listesi okunmuyor; "Otomatik temizle" tarama bitmeden basılabiliyor.

## Soru
Kullanıcı verimi açısından — ilk açılıştan "yer açıldı"ya kadar geçen süre, verilen karar sayısı, tıklama sayısı, güven ve geri dönüş isteği — DustyBytes'ı CCleaner, BleachBit, WizTree, TreeSize, Revo, BCUninstaller, Microsoft PC Manager, Storage Sense, Czkawka gibi rakiplerden açıkça iyi yapacak en etkili 10 değişiklik nedir? Her biri için: ne, neden rakipten iyi, ölçüt (nasıl ölçeriz), risk ve yaklaşık büyüklük (S/M/L). Snake-oil (kayıt temizleyici, "RAM hızlandırma") önerme. Güvenlik modelini (worker + SafetyGate + karantina) gevşetecek öneriyi açıkça işaretle. Sonda "ilk dalga" olarak yapılacak 4-5 maddeyi seç.
