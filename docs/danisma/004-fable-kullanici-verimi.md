# Danışma 004: Danışma: Kullanıcı Verimi — DustyBytes

claude-fable-5-1, 11.789 çıktı token, 170 sn. Girdi: [004-fable-kullanici-verimi-girdi.md](004-fable-kullanici-verimi-girdi.md). Cevap olduğu gibi:

---

## Danışma 004 — Kullanıcı Verimi İçin 10 Değişiklik

Ölçünün ortak paydası: **ilk açılış → "yer açıldı"** süresi, **karar sayısı**, **tık sayısı**, **güven** (geri al isteği ve pişmanlık). Bunları ölçmek için tek bir şey gerekir: uygulama yerel bir `olcum.jsonl` tutsun (ağa gitmez): süreç başlangıcı, ilk kart, tarama bitti, ilk gerçek boşalma, karar/tık/geri-al sayıları. Her madde ondaki bir sayıya bağlı.

Rakiplerin bugünkü maliyeti, aynı ölçüyle: CCleaner "Health Check" → Analiz → Temizle, arada Pro satış ekranı, geri alma yok. Storage Sense görünmez çalışır, ne sildiğini söylemez. WizTree/TreeSize harita gösterir, silme işi tek tek elle. BleachBit yüzlerce onay kutusu. Revo program başına 4-6 tık + geri yükleme noktası beklemesi. BCU sessiz kaldırmayı iyi yapar ama temizleyici değil. Czkawka her şeyi bulur, kararı tamamen kullanıcıya bırakır.

---

### 1. Açılışta Sorgusuz Tarama, İkinci Açılışta Anında Sonuç
**Ne:** İlk ekranda sürücü seçici ve "Tara" düğmesi yok; uygulama açılır açılmaz bütün sabit sürücüleri tarar, sürücü seçici sonuçların üstünde bir *filtre* olur. İkinci açılıştan itibaren son sonuç anında gösterilir, USN (dosya sisteminin değişiklik günlüğü) ile arkada tazelenir. UAC (yönetici izni penceresi) MFT okumak için zorunlu; tek seferde, üstünde bir satır gerekçeyle gelsin ("Diski hızlı okumak için izin gerekiyor"), başka hiçbir soru olmasın.
**Neden iyi:** Rakiplerin hepsi en az bir "ne tarayayım / tara" kararı ister. Burada ilk karar sıfır.
**Ölçüt:** süreç başlangıcı → ilk kart ≤ 3 sn (ilk açılış, UAC hariç), ≤ 0,5 sn (sonraki açılışlar).
**Risk:** Düşük; tarama salt okunur. Modele dokunmaz.
**Büyüklük:** S.

### 2. İki Aşamalı Tek Düğme: "Güvenli 14 GB — Temizle"
**Ne:** Tarama akarken sayaç dolan bir başlık: "Güvenle silinebilir: 14,2 GB — Temizle". Bu küme yalnızca kişisel olmayan, politikası kalıcı/karantina belli şeyler (önbellekler, WU deposu, geçici, 30 günden eski Geri Dönüşüm, eski kurulum dosyaları). Tek tık, sıfır ek soru. Altında ikinci satır: "Daha fazla yer: 86 GB, 5 karar →". Düğme tarama bitene kadar pasif ve bunu söyler ("tarama bitince açılır").
**Neden iyi:** Mevcut "Otomatik temizle" iki işi tek akışta yapıyor: güvenli olanı siliyor, sonra kart kart soruyor. Ayrılınca kullanıcı sorusuz bir kazanç alır, ardından isterse devam eder. CCleaner'ın Health Check'i buna en yakın, ama satış ekranı ve geri alma yokluğuyla.
**Ölçüt:** ilk GB boşalana kadar karar sayısı = 1, tık = 1; açılış → ilk boşalma ≤ 20 sn.
**Risk:** Düşük; kümeye yalnız bugünkü "sormadan sil" politikası girer.
**Büyüklük:** S/M.

### 3. Hedefli Mod: "Bana 60 GB Lazım"
**Ne:** Bir sayı girilir (ya da "yeni oyun için" gibi hazır hedef). Uygulama en az acı veren seti kendisi kurar ve tek plan gösterir: "60 GB için: güvenli küme 14 GB + 18 aydır açılmamış 2 oyun 49 GB. Onayla / Değiştir". Sıralama: önce güvenli, sonra en uzun süredir kullanılmayan, aynı süre içinde en büyük.
**Neden iyi:** Hiçbir rakipte yok. Kullanıcı çoğunlukla "disk doldu, şu kadar yer lazım" diye gelir; herkes ona liste verir, plan değil.
**Ölçüt:** hedefe ulaşana kadar karar sayısı ≤ 2; hedef mod oturumlarında başarı oranı (hedef karşılandı mı).
**Risk:** Orta değil, düşük — set karantinaya gider. Ama 5. maddedeki "karantina yer açmaz" sorununa doğrudan bağlı.
**Büyüklük:** M.

### 4. Kart Kart Değil, Küme Küme Karar
**Ne:** Otomatik turun ikinci yarısı kart başına soru soruyor. Yerine küme soruları: "4 oyun, 12+ aydır açılmamış, 112 GB — Hepsini karantinaya al / Seçerek / Kalsın". Küme içinde tek tıkla istisna çıkarılır. Güven düzeyi düşük olanlar ayrı bir "Emin değiliz" kümesine düşer ve asla toplu akışa girmez. Klavye: Enter = önerilen, Esc = kalsın, ok = sonraki.
**Neden iyi:** Çoğu rakipte karar sayısı dosya/program sayısına eşit. Burada küme sayısına iner.
**Ölçüt:** geri kazanılabilir alanın %80'ine ulaşmak için medyan karar ≤ 6; klavyeyle karar başına tık = 1.
**Risk:** Toplu karar yanlış bir oyunu götürebilir; karantina + 10. maddedeki oturum geri alma bunu karşılar. Modele dokunmaz.
**Büyüklük:** M.

### 5. "Yer Açıldı" Dürüstlüğü: İki Sayaç Ve "Şimdi Yer Aç"
**Ne:** Karantina aynı sürücüdeyse yer 7 gün açılmaz; "80 GB temizlendi" yazıp diskin dolu kalması güveni en hızlı kıran şeydir. Her zaman iki sayı: "Şimdi boşalan 14 GB · Karantinada 80 GB (6 gün sonra boşalır)". Yanında "Şimdi yer aç" düğmesi: karantinayı iki basışla erken boşaltır (bugünkü kural), ya da yalnız seçilen büyük öğeleri.
**⚠ Modele dokunan kısım:** Launcher'dan (Steam/Epic/GOG) yeniden kurulabilir oyunlar için kategori politikası "karantinasız sil, geri al = launcher'dan yeniden kur". Bugünkü model buna "kategori politikası" olarak izin veriyor ama geri alma artık bizim elimizde değil; yerel kayıt dosyası (cloud save kapalıysa) gider. Yapılacaksa yalnız "bulut kaydı açık" doğrulanan oyunlar ve açık bir uyarıyla. Bunu ilk dalgaya almıyorum.
**Neden iyi:** Storage Sense ve CCleaner "ne kadar sildim" der, "ne kadar boşaldı" demez.
**Ölçüt:** "temizlendi" denen GB ile gerçekten boşalan GB arasındaki fark ekranda her zaman sıfır sürprizli; "yer açılmadı" tipi geri bildirim sayısı.
**Risk:** Düşük (iki sayaç); launcher politikası orta.
**Büyüklük:** S (sayaçlar) + M (launcher politikası).

### 6. Tarama Ekranı: Akan Yol Listesi Yerine Dolan Özet, Pasif Düğmeler Düzeltmesi
**Ne:** 9 saniyelik tarama boyunca okunmayan yol akışı gitsin; yerine kategori başına sayaçların dolduğu özet (oyun 3 · 212 GB, önbellek 6 GB…) ve ilk kartlar daha tarama bitmeden okunabilir olsun. Bilinen arayüz borçları aynı işte kapanır: ikincil düğmelerin pasif görünmesi (teknesyum-ui kontrast token'ına çek), "Otomatik temizle"nin erken basılabilmesi.
**Neden iyi:** 9 saniye rakiplerin dakikalarına karşı zaten avantaj; ama boş bekletilirse avantaj hissedilmiyor. Bekleme süresi okuma süresine dönüşür.
**Ölçüt:** tarama bitmeden ilk etkileşim (kart açma, filtre) oranı; "pasif sandım" tipi yanlış tık sayısı 0.
**Risk:** Yok.
**Büyüklük:** S.

### 7. Kod İmzası
**Ne:** İlk açılış SmartScreen uyarısıyla başlıyor: "Daha fazla bilgi → Yine de çalıştır" = 2 fazladan tık ve bir korku anı; kullanıcıların bir kısmı burada bırakır. `imza-arastirma.md` zaten var; en ucuz yol (Azure Trusted Signing ya da OV sertifika) seçilip yapılsın.
**Neden iyi:** Rakiplerin hepsi imzalı; burada eşitlemek ilk açılış kaybını kapatır.
**Ölçüt:** indirme → ilk tarama tamamlama oranı; ilk açılışta tık sayısı −2.
**Risk:** Para ve kimlik doğrulama süresi; teknik risk yok.
**Büyüklük:** S.

### 8. Sessiz Kaldırma Bayrakları Ve Tek UAC'li Toplu Kuyruk
**Ne:** Kaldırıcı türünü tanı (MSI, Inno Setup, NSIS, InstallShield, Squirrel, MSIX) ve sessiz anahtarı (`/qn`, `/VERYSILENT`, `/S`…) otomatik ver; toplu kuyrukta program başına hiçbir pencere açılmasın, kalıntılar her programdan sonra karantinaya gitsin, sonda tek rapor.
**Neden iyi:** BCU bunu yapıyor ama temizleyici değil; Revo her programda sihirbaz + geri yükleme noktası bekletiyor. Burada "5 programı kaldır" = 1 tık + 1 UAC.
**Ölçüt:** kaldırılan program başına tık; toplu kuyrukta kullanıcı etkileşimi sayısı → 0.
**Risk:** Orta — sessiz kaldırıcı üreticinin kodu, bizim worker'ımız değil; durdurulamaz. SafetyGate'i gevşetmez (dosya silmeyi biz yapmıyoruz) ama yetkili süreçte yabancı kod çalışır. Tanınmayan kaldırıcıda sessiz denenmesin, görünür çalışsın.
**Büyüklük:** M.

### 9. Bildirimden Tek Tıkla Temizlik
**Ne:** Haftalık görev "disk azaldı" bildirimine eylem düğmesi: "Güvenli temizle (≈14 GB)". Tıklanınca uygulama açılır, tarar, 2. maddedeki güvenli kümeyi uygular, önce/sonra gösterir. Haftalık görev yetkisiz olduğu için sayı bir önceki taramanın tahmini; bildirimde "yaklaşık" yazar.
**Neden iyi:** Storage Sense aynı anda sessizce yapar ama ne yaptığını göstermez; CCleaner Pro Smart Cleaning ücretli.
**Ölçüt:** bildirim → boşalma ≤ 30 sn, karar = 1.
**Risk:** Düşük; görmeden siler ama yalnız güvenli küme ve karantina/politika içinde. Modele dokunmaz.
**Büyüklük:** S/M.

### 10. Oturum Geri Alma Ve Önce/Sonra Raporu
**Ne:** Karantina listesi oturuma göre kümelenir: "Bugün 14:02 — 23 öğe, 94 GB — Hepsini geri al". Her temizlik sonunda tek ekran: "Önce 48 GB boş → şimdi 62 GB · karantinada 80 GB · Geri al". Windows bildirimindeki "Geri al" aynı oturumu geri alır.
**Neden iyi:** Rakiplerin hiçbirinde oturum düzeyinde geri alma yok (Revo'nun geri yükleme noktası yavaş ve kaba).
**Ölçüt:** geri al isteklerinin karşılanma süresi; geri alınan oturum sonrası uygulamayı tekrar kullanma oranı (güven).
**Risk:** Yok.
**Büyüklük:** S.

---

### Dışarıda Bırakılanlar
Kayıt defteri temizleme, "RAM/boost", başlangıç hızlandırma vaadi: yer açmaz, ölçülemez; önermiyorum. Başlangıç programları yöneticisi disk verimiyle ilgisiz, bu danışmanın dışında.

### Ölçüm Düzeneği
Her madde için aynı 3 senaryo: temiz kurulum (0,3 M dosya), oyuncu makinesi (2,8 M dosya, 3 launcher), dolu dizüstü (%95 dolu, OneDrive açık). `olcum.jsonl`'den dört sayı: ilk kart süresi, ilk boşalma süresi, karar sayısı, tık sayısı; artı 7 gün sonra geri al sayısı. Değişiklik öncesi/sonrası aynı makinede.

### İlk Dalga (sırayla)
1. **Madde 2** — iki aşamalı tek düğme (en büyük karar/tık kazancı, S/M).
2. **Madde 1** — açılışta sorgusuz tarama + anında eski sonuç (S).
3. **Madde 5'in sayaç kısmı + Madde 10** — iki sayaç, "Şimdi yer aç", oturum geri alma (ikisi birlikte S; güven ayağı).
4. **Madde 6** — tarama ekranı ve arayüz borçları (S, zaten listede).
5. **Madde 7** — kod imzası (S, paralel gider; ilk açılış kaybını kapatır).

Madde 3 (hedefli mod) ikinci dalganın başı: ilk dalganın sayaç ve küme altyapısı olmadan "60 GB" vaadi karantina yüzünden boşa çıkar. Launcher politikası (5⚠) ve sessiz kaldırma (8) güvenlik notlarıyla üçüncü dalga.
