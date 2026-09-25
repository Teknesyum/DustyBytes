# Sistem Ve Uygulama Temizleyiciler İncelemesi

Kapsam: DustyBytes Bölüm 3 (Birimler), Bölüm 5 (Karantina) ve özellikle Bölüm 7 (Temizlik
Kuralları) için yedi kaynağın incelenmesi. `gh api` / `gh repo view` ile README, lisans,
dizin ağacı ve kaynak dosyaları okunmuştur; "ÇOK DERİN"/"DERİN" işaretlilerde asıl mekanizma
dosya:satır ile anlatılmıştır.

---

## 1. bleachbit/bleachbit (ÇOK DERİN)

**Lisans:** GPL-3.0-or-later (`COPYING`). GPL-3.0 → AGPL-3.0-or-later'a **birleşebilir**
(GPLv3 ile AGPLv3 karşılıklı uyumlu, FSF listesinde tek yönlü değil, ikisi de "or-later"
bayrağıyla birleşir). DustyBytes CleanerML kurallarını ve algoritma fikrini taşıyabilir;
`docs/licenses.md`'ye GPL-3.0 atfı ve BleachBit copyright satırı (Andrew Ziem) girilmeli.

**CleanerML biçimi** (`cleaners/*.xml`, 108 dosya, Windows'a özel: `windows_explorer.xml`,
`google_chrome.xml`, `microsoft_edge.xml`, `windows_defender.xml`, `windows_media_player.xml`
vb.): her `<cleaner id os="windows">` altında `<option>` grupları, her biri `<action
command="winreg|delete|process|...">` dizisi taşır. Örnek —
`cleaners/windows_explorer.xml` `thumbnails` seçeneği: `explorer.exe`'yi `taskkill` ile
kapatır, `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache*.db` glob'unu siler, sonra
`explorer.exe`'yi `wait="false"` ile yeniden başlatır — DustyBytes'ın "açık dosya" akışına
(Bölüm 5) doğrudan örnek: süreci durdur → sil → yeniden başlat, sessiz `kill` değil.

**winapp2.ini içe aktarma** (`bleachbit/Winapp.py:1-60`): `langsecref_map` sözlüğü winapp2.ini
`LangSecRef=` kodlarını (3021=Applications, 3025=Windows, 3029=Google Chrome…) BleachBit
kategori adlarına eşler; `configparser` ile INI ayrıştırılıp her bölüm bir CleanerML
`<cleaner>`'a dönüştürülür. Yani BleachBit winapp2.ini'yi **derleme zamanında** kendi
biçimine çevirir — DustyBytes de aynı iki katmanlı yaklaşımı (harici kural kaynağı → dahili
tip güvenli model) alabilir.

**Açık program tespiti** (`bleachbit/Cleaner.py:105-192`, `bleachbit/Process.py`):
`add_running(detection_type, pathname, same_user)` her cleaner'a bir "running" testi ekler;
`is_process_running()` (`Cleaner.py:177-192`) ya `exe` adıyla süreç listesini (`Process.py`
`is_process_running`, psutil tabanlı, Windows'ta case-insensitive karşılaştırma) ya da
`pathname` glob'unun (örn. Firefox `lock` dosyası) varlığını kontrol eder. `cleaners/firefox.xml:14-19`
örneği: `<running type="exe" os="windows" same_user="true">firefox.exe</running>` +
`<running type="pathname">~/.mozilla/firefox/*.default/lock</running>` — iki bağımsız sinyal
aynı anda tanımlanabiliyor. DustyBytes'ın Restart Manager akışına (Bölüm 5) ek olarak, program
başına "hangi exe/lock dosyası açıksa temizleme" kuralı örnek alınabilir.

**Güvenlik önlemleri — korumalı yol** (`bleachbit/ProtectedPath.py:1-80`): korumalı yollar
artık kod içine gömülü değil, ayrı `protected_path.xml` dosyasından `derinlik` (0=tam eşleşme,
1=alt klasörler…) ve `case_sensitive` alanlarıyla yüklenir, süreç ömrü boyunca önbelleğe
alınır (`_protected_paths_cache`). Bu, DustyBytes'ın "korumalı liste" verisini kod dışına,
denetlenebilir bir tanım dosyasına çıkarma fikrini destekliyor.

**"shred" (güvenli üzerine yazma)** (`bleachbit/FileUtilities.py:604-657`): `_delete_file_impl`
önce `wipe_contents()` ile dosya içeriğini sıfırlar/rastgele veriyle üzerine yazar (hard link
ise atlanır, satır 614), sonra `os.remove(wipe_name(path))` ile dosya adını da rastgele isme
çevirip siler (satır 628-630). Kilitli dosyada Windows `PermissionError winerror==32` yakalanıp
önce `_truncate_locked_file` denenir (satır 634-638). DustyBytes'ın karantina/geri dönüşüm
akışı "shred" içermiyor (silme yok, karantina var) — bu modül yalnız "kalıcı sil" adımı için
referans, karantinaya taşımaya değil.

**Yanlış silme riskleri:** BleachBit geçmişinde `windows_explorer.xml` `thumbnails` gibi
seçenekler kullanıcı uyarısı (`<warning>`) taşıyor ("bu Explorer'ı yeniden başlatır",
"masaüstü simge konumunu sıfırlar") — CleanerML her action için ayrı `<warning>` alanı
destekliyor; DustyBytes'ın "neden" satırına ek olarak yan etki uyarısı alanı eklenebilir.

**Plana somut etki:** CleanerML XML biçimi (id/option/action/warning/running) Bölüm 7'nin
"program başına temizlik tanımı elle yazılmaz" kararının doğrudan kaynağı; DustyBytes kendi
DTO'suna bu dört alanı (action türü, hedef yol, uyarı, running-testi) birebir taşıyabilir.

---

## 2. MoscaDotTo/Winapp2 (DERİN)

**Lisans:** **CC-BY-SA-4.0** (`License.md`) — kod değil, **veri** lisansı. AGPL-3.0 ile
"birleşme" kavramı yazılım kodu içindir; CC-BY-SA-4.0 veri dosyası GPL/AGPL kodun *içine
gömülmez*, ayrı bir varlık olarak birlikte dağıtılabilir, ama **paylaş-aynen-devam** şartı
taşır: DustyBytes winapp2.ini'yi olduğu gibi içe aktarıp yayınlarsa, o veri dosyasını da
CC-BY-SA-4.0 altında, atıfla birlikte dağıtmak zorunda kalır (kod lisansından bağımsız).
BleachBit projesinin bunu doğrudan gömmek yerine ayrı `bleachbit/winapp2.ini` deposunda
(BleachBit'in kendi çatalı, 106 yıldız) tutması bu ayrımı doğruluyor. **Sonuç:** DustyBytes
winapp2.ini'yi *derleme zamanı kural kaynağı* olarak kullanıp kendi CleanerML benzeri modeline
çevirebilir (BleachBit'in yaptığı gibi), ama orijinal `.ini` dosyasını ürünle birlikte
dağıtıyorsa CC-BY-SA-4.0 atfı `docs/licenses.md`'de ayrıca durmalı.

**Biçim:** klasik INI, her bölüm bir uygulama/bileşen: `LangSecRef=` (kategori kodu),
`DetectFile=` (varlık kontrolü), `FileKey1..N=yol|desen|REMOVESELF` (glob + üst klasörü de
sil bayrağı), `RegKey1..N=`, `Warning=`. Örnek (`Winapp2.ini` satır 15-19): `[Google Chrome
Autofill Data & Search Engine Preferences *]` → `DetectFile=%LocalAppData%\Google\Chrome*`,
`FileKey2=...\AutoFill*|*|REMOVESELF`.

**Bakım durumu:** aktif — son push 2026-09-15, bugüne (2026-09-25) 10 gün; **4.068 kural**
(`Winapp2.ini` başlığında "# of entries: 4,068"). 1.050 yıldız. Resmi `winapp2ool.exe` aracı
güncelleme/budama sağlıyor.

**Plana somut etki:** Bölüm 7'nin "winapp2.ini lisansı ayrıca denetlenir" notu doğrulandı —
**CC-BY-SA-4.0, doğrudan gömülemez, ayrı atıfla kaynak olarak kullanılmalı.**

---

## 3. hellzerg/optimizer

**Durum: ARŞİVLENMİŞ / TERK EDİLMİŞ.** GitHub `isArchived: true`, son push 2026-01-20.
README üstte açıkça uyarıyor: *"Optimizer is now deprecated and replaced by OptimizerNXT!"*
— proje `hellzerg/optimizerNXT`'e taşınmış (541 yıldız, GPL-3.0, son push 2026-01-22, o da
görece durgun; sadece CLI). 18.285 yıldızlı eski depo artık bakımsız.

**Lisans:** GPL-3.0 (eski depo) — AGPL-3.0 ile birleşebilir, ama kaynak kod olarak
kullanılmaması öneriliyor çünkü terk edilmiş; kavramsal ilham (gizlilik/güvenlik ayar
listesi) dışında koda güvenilmemeli.

**Plana somut etki:** Sınırlı — Optimizer bir "ayar sıkılaştırıcı" (registry tweak listesi),
DustyBytes'ın disk temizleme kapsamına değil, ayrı bir ürün kategorisine (gizlilik/telemetri
kapatma) giriyor. Plana doğrudan bir bölüm değişikliği önerilmiyor; ilgi çekiciyse ayrı bir
"gelecek" notu olarak `docs/plan.md` Açık Konular'a eklenebilir, bu incelemenin kapsamı dışı.

---

## 4. qarmin/czkawka (DERİN)

**Lisans:** İki parçalı — `LICENSE_MIT_EVERYTHING_OUTSIDE_ANY_CARGO_APP_LIBRARY` (MIT, kod)
+ `LICENSE_CC_BY_4_ICONS` (CC-BY-4.0, yalnız ikonlar). MIT, AGPL-3.0 ile tam uyumlu — algoritma
ve kod taşınabilir, yalnız MIT bildirimi korunmalı; ikonlar ayrı CC-BY-4.0 atfı ister ya da
hiç kullanılmamalı.

**Kapsadığı birimler:** `czkawka_core/src/tools/` altında `duplicate`, `empty_folder`,
`big_file`, `temporary`, `empty_files`, `bad_extensions`, `bad_names`, `invalid_symlinks`,
`similar_images`, `similar_videos`, `same_music`, `broken_files`, `exif_remover`,
`video_optimizer` — DustyBytes'ın "ertelenenler" listesindeki yinelenen dosya ve büyük dosya
aynı kod tabanında zaten çözülmüş.

**Hash kademesi** (`czkawka_core/src/tools/duplicate/mod.rs`): `PREHASHING_BUFFER_SIZE = 4 *
1024` (satır 33) — önce yalnız **ilk 4 KB**'ın hash'i alınır (`hash_calculation_limit`, satır
222), dosyalar bu ön-hash'e göre kovalara ayrılır; yalnız aynı kovadaki dosyalar için tam
`hash_calculation` (satır 297+) çalışır, `THREAD_BUFFER_SIZE = 2 MB` parça parça okunarak.
Plandaki "boyut kovası → kısmi hash → tam hash, yalnız >10 MB" kararıyla birebir aynı üç
kademeli strateji; eşik sabitleri (4 KB ön-hash, 2 MB tampon) doğrudan referans alınabilir.
Ayrıca `use_prehash_cache` + `minimal_prehash_cache_file_size` (satır 89-91) alanları,
ön-hash sonuçlarının diskte önbelleklenmesini (yeniden taramada hız) gösteriyor —
DustyBytes'ın SQLite önbelleğine eklenebilecek bir sütun fikri.

**Boş klasör / geçici dosya:** ayrı `empty_folder` ve `temporary` modülleri var; DustyBytes'ın
"geliştirici artığı" ve "önbellek/geçici" birimleriyle örtüşüyor, aynı tarayıcı geçişinde tek
seferde toplanabileceğini gösteriyor (czkawka tüm tarayıcıları tek `DirectoryEntry` ağacı
üzerinden çalıştırıyor).

**Plana somut etki:** Bölüm 3 "Ertelenenler" listesindeki yinelenen dosya kademesi, Bölüm 4
(puanlama) etkilemiyor ama Bölüm 1 (Tarama Motoru) tek geçişte çoklu sinyal toplama fikrini
doğruluyor.

---

## 5. tw93/Mole

**Lisans:** GPL-3.0 (`LICENSE`) — AGPL-3.0 ile birleşebilir. Ancak platform **macOS** (CLI Go,
native uygulama ayrı `mole.fit` — kapalı kaynak/ticari, README'de "Prefer a native app? Mole
for Mac is a separate download"). Kod taşınabilirliği düşük (Go + macOS'a özgü launchd/plist,
Spotlight); DustyBytes'a yalnız **arayüz ve kategori fikri** olarak faydalı, plan zaten bunu
öngörmüş.

**Kategori fikirleri (README):** "All-in-one CLI toolkit — CleanMyMac + AppCleaner + DaisyDisk
+ iStat Menus" tek binary'de birleşiyor: derin temizlik (önbellek/log/kalıntı), akıllı
kaldırıcı (launch agent + tercih + gizli kalıntı), disk haritası + büyük dosya, canlı
CPU/GPU/bellek/disk/ağ izleme. DustyBytes'ın Genel Bakış + Harita + Programlar + Karantina
ekran ayrımı zaten bu dört işlevi kapsıyor; Mole'un canlı sistem izleme (CPU/GPU/network)
kısmı DustyBytes planında **yok** — kapsam dışı bırakılması bilinçli bir fark, not edilmeye
değer ama plana ekleme önerilmiyor (disk temizleyici kapsamını aşar).

**Plana somut etki:** Doğrudan kod/mekanizma yok (macOS'a özgü); yalnız ekran/kategori
doğrulaması — Bölüm 8'deki ekran listesi Mole'un dört sütununa denk düşüyor, değişiklik
gerektirmiyor.

---

## 6. Storage Sense / cleanmgr / DISM (MicrosoftDocs)

Kaynak sayfa: `learn.microsoft.com/windows/configuration/storage/storage-sense`
(kaynak deposu: **MicrosoftDocs/windows-docs-pr**, dosya
`windows/configuration/storage/storage-sense.md`, `git_commit_id: 4a09a6d0`, son güncelleme
2026-02-03) ve `learn.microsoft.com/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder`
(DISM StartComponentCleanup).

**Storage Sense davranışı:** Varsayılan olarak açık, yalnız **disk alanı azaldığında**
çalışır (cadence=0); politika ile günlük/haftalık/aylık'a çevrilebilir
(`ConfigStorageSenseGlobalCadence`: 0=düşük alanda, 1=günlük, 7=haftalık, 30=aylık). Üç ayrı
eşik: bulut içerik "dehydration" (0-365 gün, erişilmeyen OneDrive dosyasını yerelden kaldırır,
buluttan silmez), İndirilenler klasörü temizliği (0-365 gün) ve Geri Dönüşüm Kutusu (0-365
gün). Hepsi CSP/GPO/Intune ile ayrı ayrı açılıp kapatılabiliyor — DustyBytes'ın "sistem
artığı: doğrudan değil, servis durdur/cleanmgr/DISM" kararına paralel: **Microsoft'un kendi
aracı bile bu üç kategoriyi birbirinden bağımsız, kullanıcı onaylı eşiklerle çalıştırıyor**,
tek bir "temizle" düğmesi yok.

**DISM `/StartComponentCleanup`:** WinSxS'teki güncelleme sonrası eski bileşen sürümlerini
kaldırır; otomatik görev (`StartComponentCleanup` Task Scheduler) **30 günlük bekleme
penceresi** uyguluyor (eski sürüm hemen silinmiyor, geri alma payı bırakılıyor).
`/ResetBase` seçeneği tüm güncellemeleri kalıcı yapar (geri alınamaz) — DustyBytes bu
bayrağı **hiç kullanmamalı**, düz `/StartComponentCleanup` yeterli ve geri dönüşü olan taraf.

**Windows.old / SoftwareDistribution güvenli yöntem:** Resmi belgede doğrudan silme
önerilmiyor; Ayarlar > Depolama > Geçici Dosyalar (Storage Sense'in bir parçası) ya da Disk
Temizleme (`cleanmgr`) "Önceki Windows kurulumları" seçeneğiyle kaldırılması öneriliyor —
elle klasör silme değil, sistem bileşeninin kendi temizlik akışı.

**Plana somut etki:** Bölüm 3 "Sistem artığı" satırındaki "Servis durdur / `cleanmgr` / DISM;
elle değil" kararı resmi belgeyle birebir doğrulandı; ek olarak DISM'de `/ResetBase`
**kullanılmamalı** notu plana eklenebilir (geri dönüşü yok, karantina felsefesiyle çelişir).

---

## 7. PolarityFlow/CacheFlow (tarayıcı önbelleği temizleyici)

`gh search repos "browser cache cleaner"` ve `"chrome cache cleaner"` taramasında en canlısı:
son push **2026-09-02** (bugüne 23 gün), C# WPF, tam DustyBytes'ın hedef ekosistemiyle aynı
dil. (Diğer adaylar: `TiagoMDG/cache_cleaner` 2023'ten beri güncellenmemiş, `edirimkus/BrowserCacheCleaner`
PowerShell script, tek dosya, 2024'ten beri durgun — ikisi de daha az canlı.)

**Lisans:** `LICENSE.txt` içeriği **MIT** (GitHub'ın repo API'si "Other" gösteriyor çünkü
telif satırı "PolarityFlow, Adrian Zingg" özel isim taşıyor, SPDX otomatik eşleşmiyor — dosya
içeriği standart MIT metni). MIT, AGPL-3.0 ile tam uyumlu.

**Desteklenen tarayıcılar** (`src/Program.cs:160-200`, tek dosya 2.478 satır): Chrome, Edge,
Brave, Vivaldi, Chromium, Opera, Opera GX (hepsi `Family="chromium"`, `User Data` kök yolu +
`Default`/`Profile N` alt klasörleri, satır 1275-1287), Firefox, LibreWolf, Waterfox (Mozilla
ailesi, profil INI'sinden okunuyor), DuckDuckGo (Microsoft Store/MSIX sürümü, `Packages`
altında, satır 131).

**Açıkken kilit / güvenlik önlemi:** `IsRunning(string procName)` (satır 1410)
`Process.GetProcessesByName` ile kontrol ediyor; UI seviyesinde her satır "çalışıyor" rozeti
taşıyor (satır 1121, 1224) ve README açıkça uyarıyor: *"A running browser locks some files;
CacheFlow skips locked files and tells you. Close the browser first for a full clean."* —
sessizce zorla kapatmıyor, DustyBytes'ın Restart Manager yaklaşımıyla aynı felsefe (kullanıcıya
sor, öldürme).

**Varsayılan olarak korunan veri:** README — şifreler, geçmiş, çerez/oturum, otomatik
doldurma **varsayılan olarak silinmez**; yalnız HTTP önbelleği, kod önbelleği, GPU/shader
önbelleği siliniyor. Kullanıcı ek kutuları işaretlerse onay istiyor. Bu, DustyBytes'ın
"tarayıcı önbellekleri ertelendi" notuna ek olarak, **hangi alt klasörlerin "güvenli" silinebilir
olduğunun somut listesini** veriyor (HTTP cache, Code Cache, GPU Cache, Service Worker cache;
places.sqlite/geçmiş asla dokunulmuyor çünkü Firefox'ta yer imleriyle aynı dosyada).

**Plana somut etki:** Bölüm 3'teki "tarayıcı önbellekleri (açıkken kilitli)" ertelenen madde
artık somut alt kapsamla (hangi klasörler güvenli, hangileri asla) genişletilebilir; A5
(Temizlik) aşamasına, ertelenen değil erken alınabilecek düşük riskli bir birim olarak
taşınması düşünülebilir.

---

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 7. Temizlik Kuralları | CleanerML dört alanı (action/warning/running-testi/id) DTO'ya birebir taşınabilir | bleachbit/bleachbit |
| 7. Temizlik Kuralları | winapp2.ini CC-BY-SA-4.0 — gömülmez, derleme zamanı kaynak + ayrı atıf | MoscaDotTo/Winapp2 |
| 5. Karantina | Program başına "exe adı + lock dosyası" ikili running-testi örneği | bleachbit/bleachbit (Cleaner.py, firefox.xml) |
| 5. Karantina | Korumalı yol listesi kod yerine ayrı XML/JSON tanım dosyasında tutulabilir | bleachbit/bleachbit (ProtectedPath.py) |
| 3. Birimler (Ertelenenler) | Yinelenen dosya hash kademesi sabitleri (4 KB ön-hash, 2 MB tampon, önbellek sütunu) | qarmin/czkawka |
| 3. Birimler (Sistem artığı) | DISM `/StartComponentCleanup` evet, `/ResetBase` asla; Windows.old elle değil `cleanmgr`/Storage Sense üzerinden | MicrosoftDocs/windows-docs-pr |
| 3. Birimler (Ertelenenler) | Tarayıcı önbelleği alt kapsamı netleşti (HTTP/Code/GPU/ServiceWorker cache güvenli, geçmiş/şifre asla) — erken alınabilir birim adayı | PolarityFlow/CacheFlow |
| Açık Konular | hellzerg/optimizer terk edilmiş, optimizerNXT'e taşınmış; kod referansı alınmamalı | hellzerg/optimizer |
