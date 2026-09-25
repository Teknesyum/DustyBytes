# Disk Çözümleyici Depoları İncelemesi

Yedi açık kaynak disk-kullanımı aracının tarama mekanizması, lisans uyumluluğu ve
arayüz tasarımı DustyBytes planına (Bölüm 1: Tarama Motoru, Bölüm 8: Arayüz/Harita)
girdi olması için incelendi. Veriler `gh api` ile 2026-09-25 tarihinde çekildi.

## Windirstat/Windirstat (Derin)

- **Yıldız:** 4118. **Son commit:** 2026-09-25 (aktif). **Lisans:** GPL-2.0, başlıkta
  "version 2 of the License, or at your option any later version" → fiilen
  **GPL-2.0-or-later**.
- **Uyumluluk:** GPL-2.0-or-later, AGPL-3.0-or-later ile uyumlu (GPLv3'e yükseltilip
  birleştirilebilir). Kod alınabilir, atıf ve lisans metni korunmalı.
- **Tarama mekanizması:** İki ayrı "Finder" sınıfı var.
  - `windirstat/FinderBasic.cpp:28-249` — varsayılan yol. `NtQueryDirectoryFile`
    (ntdll, `FileIdFullDirectoryInformation`/`FileFullDirectoryInformation`) kullanıyor,
    klasik `FindFirstFile` değil, daha düşük seviyeli NT API. Yerel diskte 4 MiB, uzak
    (UNC/ağ) birimde 64 KiB tampon (satır 41-42, 51-53) — SMB redirector'larla uyumluluk
    için boyut küçültülüyor (yorum: "issue #631"). `FinderBasic.cpp:150-182` reparse
    point işleme: `IO_REPARSE_TAG_MOUNT_POINT` hem junction hem volume mount point için
    dönebildiğinden, `FSCTL_GET_REPARSE_POINT` ile buffer okunup `IsJunction()` çağrılarak
    ayrıştırılıyor.
  - `windirstat/FinderNtfs.cpp:162-435` — "hızlı kip". Volumu `FILE_FLAG_NO_BUFFERING |
    FILE_FLAG_OVERLAPPED` ile açıp `FSCTL_GET_NTFS_VOLUME_DATA` ve
    `FSCTL_GET_RETRIEVAL_POINTERS` ile `$MFT::$DATA`'nın disk üzerindeki data run'larını
    (cluster aralıklarını) buluyor, sonra bu run'ları **`std::for_each(std::execution::par, ...)`**
    ile paralel okuyup (satır 412) MFT kayıtlarını satır satır (512 byte sektör fixup
    dahil, satır 285-303) elle çözümlüyor. Bu, planın "NTFS MFT doğrudan okuma" hedefiyle
    birebir örtüşüyor; USN Journal kullanımı bu dosyada yok (tam tarama MFT'den, artımlı
    güncelleme ayrı bir mekanizma olmalı — repoda görülmedi).
- **Hardlink/reparse:** `FinderNtfs.cpp:308-317` `BaseFileRecordNumber` üzerinden aynı
  taban kaydına (`m_baseFileRecordMap`) sahip tüm hardlink'leri tek `FileRecordBase`'e
  topluyor — dosya kimliğiyle bir kez sayma planın istediğiyle aynı yaklaşım.
- **Treemap çizimi:** `windirstat/Controls/TreeMapLayout.cpp:103-182` klasik "squarified
  treemap" (Bruls/Huizing/van Wijk) algoritması + ayrıca `Style::Hilbert`/`Style::Moore`
  seçenekleri (`HilbertMooreTreeMap.cpp`) — uzay dolduran eğri tabanlı, komşuluğu koruyan
  alternatif bir düzen. DustyBytes'ın tek-çizim + elle hit-test yaklaşımıyla uyumlu; kod
  satırları referans alınabilir.
- **Performans iddiası:** README'de somut benchmark yok, sadece "hızlı kip" tanımı var.
- **UX fikri:** Squarified dışında Hilbert/Moore eğrisi seçeneği sunmak, kullanıcıya
  düzen tercihi vermek iyi bir fikir.
- **Tuzak:** UNC/ağ sürücülerinde büyük tampon bazı redirector'larda başarısız oluyor
  (issue #631) — DustyBytes de büyük buffer kullanıyorsa ağ sürücüsünü tespit edip
  tamponu küçültmeli.

## Dundee/Gdu (Derin)

- **Yıldız:** 6010. **Son commit:** 2026-09-23 (aktif). **Lisans:** MIT.
- **Uyumluluk:** MIT, AGPL-3.0-or-later ile tam uyumlu, doğrudan kod alınabilir.
- **Tarama mekanizması:** `pkg/analyze/parallel.go:69-211` — dizin başına goroutine
  (`go a.processQueuedDir(...)`, satır 108), eşzamanlılık `concurrencyLimit = make(chan
  struct{}, 2*runtime.GOMAXPROCS(0))` kanalıyla sınırlanıyor (satır 13, 50-59) — yani
  "dizin başına paralel" modeli DustyBytes'ın planıyla birebir aynı fikir, ama Windows'a
  özel bir MFT/USN yolu **yok**: dizin okuma `os.ReadDir` (Go standart kütüphanesi,
  içeride `FindFirstFile` kullanır) ile yapılıyor (satır 81).
- **Hardlink/reparse/bulut:** `pkg/analyze/dir_other.go:11-29` (Windows derlemesi) —
  blok bilgisi olmadığından kullanım = görünen boyut (`ino=0`, yani **hardlink dedup
  Windows'ta yok**, sadece Unix `dir_unix.go`'da inode ile yapılıyor — önemli bir eksik/
  tuzak). Aynı dosyada satır 20-28: `FILE_ATTRIBUTE_RECALL_ON_OPEN` /
  `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` bit'leri kontrol edilip OneDrive bulut yer
  tutucu dosyalarının boyutu **0'a çekiliyor** — DustyBytes'ın "bulut yer tutucusu diskte
  yer tutmaz, ayrı gösterilir" maddesiyle doğrudan örtüşen, kopyalanabilir bir mantık
  (`pkg/analyze/dir_other.go:20-28`). `pkg/analyze/symlink.go:14-23` sembolik bağlar
  `os.Readlink` ile hedefi okuyor ama içine girmiyor (varsayılan), `followSymlinks`
  bayrağı varsa `filepath.EvalSymlinks` ile takip ediliyor.
- **Silme:** `pkg/remove/trash_windows.go` Windows'a özel "çöp kutusuna taşı" komutu var
  (ayrı dosya, ayrı derleme etiketiyle).
- **Performans iddiası (README, "Benchmarks" bölümü):** 90 GB / 100k dizin / 400k dosya
  üzerinde hyperfine ile ölçüm var: soğuk önbellekte `gdu -npc` 4.72 s (`diskus`'a göre
  1.05x), `dua` 6.03 s, `dust -d0` 6.18 s, klasik `du -hs` 30.6 s, `ncdu` 33.2 s. Sıcak
  önbellekte `gdu -npc` 466 ms, `dua` 591 ms, `du -hs` 1255 ms. Yani goroutine-per-dizin
  modeli, sıralı `du`'ya göre ~5-7x hızlı; harici DB (badger/sqlite) modu **çok daha
  yavaş** (44 s) — kalıcı önbellek DustyBytes için cazip görünse de doğrudan disk DB'si
  performansı ciddi düşürüyor.
- **Tuzak:** Windows'ta hardlink dedup'ının tamamen atlanması — DustyBytes planı
  hardlink'i kimlikle bir kez saymayı öngördüğü için gdu'nun bu boşluğuna düşmemek
  gerekir (MFT FileId zaten `windirstat` gibi bunu çözüyor).

## Bootandy/Dust (Normal)

- **Yıldız:** 12334. **Son commit:** 2026-09-16 (aktif). **Lisans:** Apache-2.0.
- **Uyumluluk:** Apache-2.0, AGPL-3.0-or-later ile tek yönlü uyumlu (Apache-2.0 kodu
  AGPLv3 projesine katılabilir; tersi geçerli değil). Kod alınabilir, NOTICE/atıf
  korunmalı.
- **Tarama:** `Cargo.toml`'da `rayon = "1"` bağımlılığı var — iş çalma (work-stealing)
  tabanlı veri-paralel tarama; özel bir Windows API kullanımı görünmüyor (std
  `fs::read_dir` + rayon üzerine kurulu tipik Rust yaklaşımı).
- **UX fikri:** README demo'sunda öne çıkan somut fikir — her satırda yüzde çubuğu
  gösterilirken, bir alt dizine inildiğinde üst dizinin oranı **gölge (shadow)** olarak
  çubukta kalıyor ("bar jumps to 44%... a shadow stays at 44% showing it is part of the
  src folder"). Kullanıcı derine indikçe bağlamı kaybetmiyor — DustyBytes'ın treemap
  breadcrumb'ına ek olarak liste/özet görünümünde de uygulanabilir bir fikir.
- **Performans:** Kendi README'sinde iddia yok; gdu'nun benchmarkında `dust -d0` sıcak
  önbellekte 579 ms, soğukta 6.18 s ölçülmüş (bkz. gdu bölümü).

## Byron/Dua-Cli (Derin)

- **Yıldız:** 6297. **Son commit:** 2026-09-25 (aktif). **Lisans:** MIT.
- **Uyumluluk:** MIT, tam uyumlu, doğrudan kod alınabilir.
- **Tarama mekanizması:** Asıl paralel yürüyüş, ayrı yayınlanmış bir crate olan
  `dua_core`'a taşınmış (`crates/dua-lib/src/lib.rs:7`: `pub(crate) use dua_core as
  walk;`) — bu repoda kaynak yok, ama kullanım şekli `src/traverse.rs:1628-1710`
  (`walk_clean_candidates`) içinde görülüyor: `std::thread::scope` ile bir "discovery"
  thread'i açılıyor, `crate::walk::stream_roots(options.threads - 1, Order::ParentFirst,
  ...)` çağrısıyla iş parçacığı bütçesi (`options.threads`) paylaşılıyor, sonuçlar
  `crossbeam::channel` üzerinden akış (streaming) olarak `TraversalEvent`'lere
  dönüştürülüyor — DustyBytes'ın "sonuç akışla gelir, iptal her an mümkün" hedefiyle
  aynı model (`Ordering::Relaxed` ile paylaşılan `AtomicBool` iptal bayrağı, satır
  1652 ve 1688-1689).
- **Hardlink:** `src/traverse.rs:822,1217-1235` `InodeFilter` (`src/inodefilter.rs`) ile
  paylaşılan inode'lar tek sefer sayılıyor; `commit_clean_inodes` fonksiyonu "clean"
  (temiz/doğrulanmış) girişleri kabul ederken hardlink muhasebesini erteliyor
  (`deferred_inodes`, satır 837-838) — sıra dışı tamamlanan (out-of-order) paralel
  görevlerde çifte sayımı önlemek için tasarlanmış, ilginç bir eşzamanlılık deseni.
- **Silme/onay akışı:** `src/interactive/app/deletion.rs:87-226`
  (`delete_paths`/`delete_directory_recursively`) — `trash-move` özelliği açıksa önce
  `trash::delete(&path)` (işletim sistemi çöp kutusu) deneniyor (satır 103-106), başarısız
  olursa `fs::remove_file`/`remove_dir_all` ile kalıcı silmeye düşüyor. Testler
  (`journeys_deletion.rs`) iptal sırasında mevcut silme işleminin tamamlanıp kalan
  hedeflerin korunduğunu doğruluyor (satır 422 civarı, "cancellation finishes the
  current removal and preserves remaining targets") — DustyBytes'ın "iptal her an
  mümkün" ilkesi silme akışına da uygulanmalı.
- **Tuzak:** Asıl yürüyüş mantığı ayrı bir crate'e (`dua_core`) taşınmış olması, bu
  reponun kendi başına "paralel tarama" referansı olarak eksik kalmasına yol açıyor —
  ihtiyaç olursa `dua_core` crate'i ayrıca incelenmeli.

## Shundhammer/Qdirstat (Normal — Treemap + Cleanup)

- **Yıldız:** 2595. **Son commit:** 2026-09-24 (aktif). **Lisans:** GPL-2.0, dosya
  başlıklarında sadece "License: GPL V2" yazıyor, "or later" ibaresi **yok**
  (`src/Cleanup.cpp:1-6`) → **GPL-2.0-only**.
- **Uyumluluk:** GPL-2.0-only, AGPL-3.0-or-later ile **uyumsuz** — GPL-2-only kod
  AGPLv3 eserine birleştirilemez (FSF uyumluluk matrisi). Kod kopyalanamaz, sadece
  mimari fikir olarak incelenebilir.
- **Treemap:** `doc/Treemap.md` içinde algoritma anlatılmıyor ama etkileşim modeli
  net: orta tık ile seçili öğenin üst dizinlerini (ebeveyn, büyükebeveyn...) beyaz
  çerçeveyle vurgulayıp geri kalanı soluklaştırma ("Showing the Hierarchy", satır
  107-120), ağaç görünümü ile treemap'in çift yönlü senkron seçimi (tıklanan öğe her
  iki görünümde birden vurgulanıyor), MIME kategorisine göre renklendirme (dokümanlar
  mavi, resimler camgöbeği, video açık yeşil vb., kullanıcı tanımlı).
- **Cleanup eylem sistemi:** `src/Cleanup.h:28-51` — sabit "sil" eylemi yerine, her biri
  bir kabuk komutu (`%p`/`%n` yer tutuculu), bir `RefreshPolicy`
  (`NoRefresh/RefreshThis/RefreshParent/AssumeDeleted`) ve bir `OutputWindowPolicy`
  (çıktı penceresini her zaman/hata varsa/zaman aşımından sonra/asla göster) taşıyan
  **kullanıcı tanımlı eylem listesi** (`src/StdCleanup.cpp`, `CleanupCollection`).
  "Çöp kutusuna taşı" ve "kalıcı sil" bunlardan sadece ikisi; kullanıcı kendi komutlarını
  (ör. "7-Zip ile sıkıştır", "burada terminal aç") ekleyebiliyor.
- **UX fikri:** Yapılandırılabilir eylem listesi modeli DustyBytes'ın "silme" ekranına
  genişletilebilir bir taban olarak önerilebilir (kod alınmadan, sadece tasarım fikri
  olarak — lisans uyumsuzluğu nedeniyle).
- **Tuzak:** GPL-2.0-only lisans nedeniyle bu depodan **kod satırı kopyalanmamalı**.

## KDE/Filelight (Normal — Halka Grafik)

- **Yıldız:** 290. **Son commit:** 2026-09-24 (aktif, KDE altında bakımlı). **Lisans:**
  Dosya başına SPDX: `GPL-2.0-only OR GPL-3.0-only OR LicenseRef-KDE-Accepted-GPL`
  (`src/radialMap/map.cpp:1-6`).
- **Uyumluluk:** "OR" ile üçlü seçenek sunulduğu için **GPL-3.0-only** seçilerek
  AGPL-3.0-or-later ile birleştirilebilir (GPLv3 ve AGPLv3 karşılıklı birleşim izni
  içerir) — ama kod alınacaksa hangi seçenek altında kullanıldığı belgelenmeli.
- **Halka çizim mantığı:** `src/radialMap/map.cpp:215-273` (`Map::build`) — her derinlik
  seviyesi bir halka (ring); her dosya/dizin, boyutuyla orantılı bir açı diliminde temsil
  ediliyor: `a_len = MAX_DEGREE * (size / root->size())` (satır 238). Açısal genişliği
  belirli bir eşiğin altında kalan dosyalar (`file->size() < m_limits[depth] * 6`, "yarım
  derece" eşiği, satır 228) tek tek çizilmiyor, tek bir "FilesGroup" diliminde
  toplanıyor (satır 262-269) — çok sayıda mikroskobik dilimin görsel/performans
  sorununu böyle çözüyorlar. Renklendirme (`colorise()`, satır 308+) derinliğe göre
  koyulaşan bir gradyan ve açısal konuma göre değişen ton (hue) kullanıyor; Windows
  derlemesinde vurgu rengi `winrt::Windows::UI::ViewManagement::UISettings` üzerinden
  sistem aksan rengine bağlanıyor (satır 326-329) — Windows tema entegrasyonu için
  doğrudan uygulanabilir bir örnek.
- **UX fikri:** Küçük dosyaları otomatik gruplama eşiği (görsel gürültüyü azaltma),
  DustyBytes'ın treemap'inde de "çok küçük kutucukları grupla" seçeneği olarak
  uygulanabilir.
- **Tuzak:** Halka (ring) grafik, derin dizin ağaçlarında dıştaki halkalar gittikçe
  incelip okunaksızlaşıyor — bu yüzden Filelight de `defaultRingDepth` ile görünür
  derinliği sınırlıyor (satır 192-205); DustyBytes treemap kullandığı için bu sorunu
  zaten yaşamaz ama "aşırı derin dizin" durumunda benzer bir görünür-derinlik sınırı
  faydalı olabilir.

## Adileo/Squirreldisk (Normal — Modern Arayüz Yaklaşımı)

- **Ad doğrulaması:** `adileo/squirreldisk` doğru isim (GitHub aramasında teyit edildi;
  bir de bakımsız fork'u `Boute95/TamiaDisk` var).
- **Yıldız:** 1827. **Son commit:** 2023-08-04 — **2+ yıldır güncellenmemiş, bakımsız**.
  **Lisans:** AGPL-3.0.
- **Uyumluluk:** AGPL-3.0, DustyBytes ile aynı lisans ailesi — tam uyumlu, kod
  alınabilir (JS/TS/Rust, C#/Avalonia'ya doğrudan taşınamaz ama desen olarak).
- **Mimari:** Adı "Tauri/Rust" olsa da gerçek yapı **Tauri kabuğu + React/TypeScript/d3
  arayüz + harici bir Rust CLI aracı**: `package.json` bağımlılıkları `@tauri-apps/api`,
  `react`, `d3`, `react-beautiful-dnd`; tarama işi kendi kodlarında değil,
  `src-tauri/bin/pdu-x86_64-pc-windows-msvc.exe` gibi **platforma göre önceden
  derlenmiş `pdu` (parallel-disk-usage) ikili dosyaları** repoya gömülü halde
  çalıştırılıyor.
- **UX fikri:** d3 tabanlı treemap + React ile sürükle-bırak (`react-beautiful-dnd`) —
  muhtemelen dosya/dizin gruplama veya favori düzenleme içindir; DustyBytes'ın
  Avalonia/Skia mimarisine doğrudan taşınamaz ama "harici tarama motorunu ayrı süreçte
  çalıştır" fikri DustyBytes'ın worker/UI ayrımıyla zaten örtüşüyor.
- **Tuzak (önemli):** Taramayı kendi koduyla değil, repoya gömülü üçüncü taraf
  önceden-derlenmiş bir `.exe` ile yapması hem tedarik zinciri güveni hem lisans
  şeffaflığı açısından kötü bir örnek — DustyBytes worker'ı kendi derlediği koddan
  oluşmalı, gömülü üçüncü taraf ikili dosya kullanılmamalı. Ayrıca projenin 2+ yıldır
  bakımsız kalması, bu deseni tek başına örnek almanın riskli olduğunu gösteriyor.

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 1: Tarama Motoru | Hızlı kip için MFT data run'larını `NtQueryDirectoryFile`/`FSCTL_GET_RETRIEVAL_POINTERS` ile paralel okuma deseni referans alınabilir (satır düzeyinde) | windirstat FinderNtfs.cpp:162-435 |
| 1: Tarama Motoru | Junction ile volume mount point'i ayırt etmek için `FSCTL_GET_REPARSE_POINT` + `IsJunction()` ayrıştırması uygulanmalı | windirstat FinderBasic.cpp:150-182 |
| 1: Tarama Motoru | Ağ/UNC sürücülerde tampon boyutu küçültülmeli (büyük tamponlar bazı redirector'larda başarısız oluyor) | windirstat FinderBasic.cpp:41-53 |
| 1: Tarama Motoru | Dizin başına paralel tarama sınırı `2×CPU çekirdek sayısı` gibi bir üst sınırla kanal/semafor ile kısılmalı, sınırsız goroutine/thread açılmamalı | gdu parallel.go:13,50-59 |
| 1: Tarama Motoru | OneDrive/bulut yer tutucu tespiti `FILE_ATTRIBUTE_RECALL_ON_OPEN`/`RECALL_ON_DATA_ACCESS` bit kontrolüyle doğrulandı, plan bu haliyle doğru | gdu dir_other.go:20-28 |
| 1: Tarama Motoru | Sonuç akışını kanal/queue üzerinden iptal edilebilir şekilde yayınlama deseni (Atomic iptal bayrağı + streaming event) doğrulandı | dua-cli traverse.rs:1628-1710 |
| 1: Tarama Motoru | Silme akışında iptal edildiğinde "mevcut işlemi bitir, kalanları koru" kuralı planın "iptal her an mümkün" maddesine eklenmeli | dua-cli deletion.rs (journeys_deletion.rs testleri) |
| 8: Arayüz/Harita | Çok küçük öğeleri (belirli bir alan/açı eşiğinin altı) tek tek çizmek yerine "diğerleri" grubunda toplama seçeneği eklenmeli | filelight map.cpp:228,262-269 |
| 8: Arayüz/Harita | Treemap'te seçili öğenin ebeveyn zincirini vurgulayıp geri kalanı soluklaştırma (breadcrumb'a ek olarak) değerlendirilmeli | qdirstat Treemap.md:107-120 |
| 8: Arayüz/Harita | Özet/liste görünümünde üst dizin oranını "gölge" olarak koruma (bağlam kaybını önleme) fikri değerlendirilmeli | dust README demo |
| 8: Arayüz/Harita | Silme dışında yapılandırılabilir "eylem" (cleanup action) sistemi ileride genişletme olarak not edilmeli (kod alınmadan, sadece tasarım) | qdirstat Cleanup.h:28-51 |
| — (uyarı) | Worker'da gömülü/önceden derlenmiş üçüncü taraf ikili dosya çalıştırma deseninden kaçınılmalı | squirreldisk src-tauri/bin/pdu-*.exe |
