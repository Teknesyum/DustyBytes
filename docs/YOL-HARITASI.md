# Yol Haritası

Sahibin söylediği ve bitmemiş işler. Biten silinmez, işaretlenir. Ayrıntı: [plan.md](plan.md).

- [x] Plan ve fable görüşü ([001](danisma/001-fable-dustybytes-plan.md))
- [x] İnceleme: 61 depo, yedi rapor (`docs/inceleme/`)
- [x] Plan sürüm 2: inceleme sonrası revizyon
- [x] A0 İskelet
- [x] A1 Salt Okunur Tarama
- [x] A2 Güvenli Silme
- [x] A3 Sinyal ve Puan
- [x] A4 Kaldırıcı
- [x] A5 Temizlik
- [x] A6 Hızlı Tarama
- [x] A7 Yayın — v0.1.1 yayında; kod imzası açık, sertifika sahibin kararı
- [x] A9 Fark: büyük içerik tanıyıcıları, tek tık karantina, 7 gün, onaysız boşaltma — 2026-09-27, denetim docs/ui-denetim/2026-09-27d.md
- [x] A10 Oyunlarda tek tık karantina, hızlı açılış
- [x] A11 Kalıcı silme (tehlike onayıyla), büyük metin, ekranı kaplayan pencere — v0.4.0
- [x] A12 Hızlı Tarama Akışı ([002](danisma/002-fable-dustybytes-tarama-akisi.md)): kaydetme arka planda; hızlı taramada SQLite yerine ikili aktarım; USN artımlı yenileme varsayılan; yardımcı ayaktaysa MFT; ağaçtan bağımsız türler tarama sürerken görünür
- [x] A13 Kalıntısız Kaldırma: tek onayla kaldır ve yüksek güvenli kalıntıyı yedekle karantinaya al; sessiz kaldırıcı, değer düzeyi kayıt, COM ve kabuk uzantıları, isteyen kullanıcının kaydı — 2026-09-28, ölçüm tezgâhı `tools/uninstall-bench/` gerçek makinede çalıştırılmadı
- [x] A14 Kolay Akış Ve Otomatik Mod: kalıcı silme iki basışla, tek satır öneriler, üç durumlu tümünü seç, otomatik temizlik ve tur, hareket token'larıyla kart geçişi — 2026-09-28, gerçek makinede silme denenmedi
- [x] A15 Piyasanın En Pratiği: bütün sürücüler, Geri Dönüşüm Kutusu, eski indirilenler, hazırda bekletme, Küçült ve OneDrive yalnız çevrimiçi, haftalık hatırlatma ve sağ tık, kopya dosyalar, zorla ve toplu kaldırma — 2026-09-30, gerçek makinede Geri Dönüşüm boşaltma, powercfg, bildirim, OneDrive çözme ve toplu kaldırma denenmedi
- [x] A16 Kullanıcı Verimi: tek düğme güvenli temizlik, hedefli mod, küme karar ve klavye, dürüst sayaç, oturum geri alma ve önce/sonra, önizleme = yürütme, ne büyüdü, sade açıklamalar ve çerez koruma, sessiz kaldırma, bütçeli bildirim, yerel ölçüm — 2026-10-02, gerçek makinede bildirim düğmesi ve sessiz kaldırma denenmedi
- [ ] A8 İngilizce Arayüz — ertelendi: önce Türkçe arayüz tam yetkin olacak; sonra metinler locale/tr.json ve en.json'a taşınır, dil seçimi eklenir
