# Arayüz Bağımsız İnceleme 1

Gözden geçiren: arayüzü yazmamış ajan (a172d298d5fd26dc4). Girdi: `tmp/gercek-pencere.png` (848×640, 96 dpi, prova kipi) ve `tmp/uc/` headless çekimleri.

**çalışıyor:** evet. Gerçek pencere açılıyor ve gerçek tarama verisini gösteriyor (155 GB, 6254 birim). Headless çekimlerde yedi ekranın hepsi dolu.

**kullanılabilir:** evet, bir eksikle. 848×640'ta birincil eylem görünüyor, yatay kaydırma yok. "Türe göre" listesi gerçek makinede yanlış döküm veriyor.

1. Türe göre — yüksek: Oyun ve Program yok; Steam oyunları Klasör ya da Geliştirici altına düşüyor olabilir, 72,9 GB Geliştirici şüpheli.
2. Bekleyen alanı — orta: Genel Bakış "Okunmadı" / Karantina "5,59 GB, 2 öğe" / gerçek "0 B, 0 öğe".
3. Harita — orta: dilimler tek renk, gösterge boşa; boyutlar mono değil.
4. Alt satır ayracı — orta: "Oyun ,  Son kullanım".
5. Başlıklarda g kuyruğu kesiliyor — orta.
6. En küçük boyutta İlk beş teklif ilk satırı kesik — düşük.
7. Sayı biçimi — düşük: alt çubuk mono değil, "6254" binlik ayraçsız.
8. Öneriler/Temizlik — düşük: onay kutusuz kartlar hizasız, Sistem temizliği iç boşluğu farklı, Winapp2 etiketi açıklamasız, pasif düğme kontrastı zayıf.
