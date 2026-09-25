# MFT Ve Hızlı Dosya İndeksi — Açık Kaynak İnceleme

Kapsam: NTFS MFT okuma, USN günlüğü, hızlı dosya indeksi. İlgili plan bölümleri: "1. Tarama
Motoru" ve "A6 Hızlı Tarama" (`docs/plan.md`).

## 1. omerbenamram/mft — DERİN

Rust NTFS $MFT ayrıştırıcısı. Son push 2026-01-03, 162 yıldız, 28 fork, 19 açık issue, arşivlenmemiş.

**Lisans/AGPL uyumu:** `MIT/Apache-2.0` (dual), `LICENSE-APACHE` + `LICENSE-MIT`. AGPL-3.0-or-later
ile tam uyumlu. Rust olduğu için C#'a bağımlılık olarak girmez, yalnız algoritma referansı olur.

**Mekanizma (dosya:satır):**
- Entry header: `src/entry.rs:112-158` — signature, `usa_offset`/`usa_size`, `hard_link_count`,
  `EntryFlags`, `base_reference`.
- Fixup (USA): `src/ntfs.rs:45-118` — sabit 512 bayt stride ile her sektörün son 2 baytını
  USA dizisindeki orijinal baytla değiştirir; uyumsuzlukta best-effort devam eder. Çağrı noktası
  `src/entry.rs:271-290`.
- Attribute döngüsü: `src/entry.rs:306-408`, `0xFFFFFFFF` ($END) ile durur.
- Resident/non-resident ayrımı: `src/attribute/header.rs:73-168` (`form_code`).
- $STANDARD_INFORMATION (0x10): `src/attribute/x10.rs:39-96` — 48/72 bayt otomatik algılama.
- $FILE_NAME (0x30): `src/attribute/x30.rs:77-123` — `logical_size`/`physical_size`,
  `FileAttributeFlags`, `reparse_value`, `namespace`.
- $DATA / data run: `src/attribute/non_resident_attr.rs:14-51` + `src/attribute/data_run.rs`
  (VCN→LCN run listesi).
- Boyut alanları: `src/attribute/header.rs:195-218` — `allocated_length`, `file_size`,
  `valid_data_length`; sıkıştırma varsa ek `total_allocated` (119-123, 232-236).
- Sıkıştırma/sparse bayrakları: `attribute/mod.rs:243-251` (`IS_COMPRESSED`, `SPARSE`, `ENCRYPTED`),
  attribute header'ın `data_flags` alanından (`header.rs:93`).

**Gereken yetki:** Crate ham volume handle açmıyor, yalnız `Read+Seek` alıyor
(`MftParser::from_read_seek`, `src/mft.rs:13-70`) — `\\.\C:` açma/admin tamamen tüketicinin işi.

**Hardlink/reparse/sparse:** Hardlink `hard_link_count` (`entry.rs:84,132,149`) doğrudan header'dan;
birden fazla 0x30 attribute otomatik tekilleştirilmiyor, tüketici karar verir. Reparse point
(0xC0) enum'da tanımlı ama özel struct'a ayrıştırılmıyor (`attribute/mod.rs:73-76,195`); asıl
tespit `FILE_ATTRIBUTE_REPARSE_POINT` (0x400) bayrağı üzerinden. Cloud placeholder bayrakları
(`RECALL_ON_DATA_ACCESS=0x40_0000`, `OFFLINE=0x1000`, `PINNED`/`UNPINNED`) aynı enum'da mevcut —
plandaki "bulut yer tutucu ayrı gösterilir" ile doğrudan örtüşüyor.

**Artımlı güncelleme:** Yok — tek seferlik MFT snapshot ayrıştırıcısı, USN Journal desteği içermiyor.

**Ölçülen hız:** Yok, sadece boş bir benchmark iskeleti var, rakam paylaşılmamış.

**Tuzaklar:** `entry_size` ilk kayıttan tahmin ediliyor (`mft.rs:16-17,52`), volume header'dan
okunmuyor — standart dışı MFT'de yanlış boyut riski. Fixup uyumsuzluğu sessizce geçiyor.

**DustyBytes'a somut etki:** Fixup, resident/non-resident, boyut offsetleri C#'a algoritma olarak
taşınabilir. Hardlink/reparse/cloud-placeholder dedup DustyBytes'ın kendi file_reference
(entry+sequence) katmanında yazılmalı, crate bunu vermiyor.

## 2. microsoft/CsWin32

Son push 2026-09-23, 2521 yıldız, 124 fork.

**Lisans/AGPL uyumu:** MIT. Tam uyumlu.

**Mekanizma:** `NativeMethods.txt`'ye fonksiyon adı eklemek yeterli
(`docfx/docs/getting-started.md:47-58`). Üretilen `DeviceIoControl` imzası
(`test/CsWin32Generator.Tests/CsWin32GeneratorTests.cs:245`):
`SafeHandle hDevice, uint dwIoControlCode, [Optional] ReadOnlySpan<byte> lpInBuffer,
[Optional] Span<byte> lpOutBuffer, out uint lpBytesReturned, [Optional] NativeOverlapped* lpOverlapped`
— `Span<byte>`/`SafeHandle` tabanlı modern imza, manuel `Marshal.AllocHGlobal` gerekmiyor. Örnek
`NativeMethods.txt` girdisi: `test/GenerationSandbox.BuildTask.Tests/NativeMethods.txt:19`.

**FSCTL_ENUM_USN_DATA / FSCTL_GET_NTFS_VOLUME_DATA üretimi — DOĞRULANAMADI.** Repo içinde
(kaynak, test, docs) bu isimler hiç geçmiyor. Sebep: gerçek struct/sabit tanımları ayrı
`microsoft/win32metadata` deposundan (winmd) geliyor, bu repodan doğrulanamaz. Gerekli: küçük bir
spike — `NativeMethods.txt`'ye `DeviceIoControl` + `FSCTL_ENUM_USN_DATA` + `MFT_ENUM_DATA_V0`
yazıp gerçek derleme denemek.

**Gereken yetki:** N/A (jeneratör); admin gereksinimi çağrılan FSCTL koduna bağlı.

**Boyut/hardlink/reparse/artımlı/hız:** İlgisiz — bu depo yalnız imza üretir.

**Tuzaklar:** FSCTL struct'larının üretilebilirliği doğrulanmadı, ilk adımda spike şart.

**DustyBytes'a somut etki:** "Hızlı kip (yönetici)" için `DeviceIoControl` P/Invoke'unu elle
yazmak yerine tek satır `NativeMethods.txt` girdisiyle üretmek mümkün — ama FSCTL sabitlerinin
üretilebilirliği doğrulanana kadar bu adım riskli/varsayımlı sayılmalı.

## 3. wangfu91/UsnParser — DERİN

C#, MIT, 23 yıldız, son push 2026-05-27 (aktif, .NET 10). (Alternatif olarak değerlendirilen
michaelkc/NtfsReader — LGPL-2.1, 35 yıldız — FSCTL_ENUM_USN_DATA kullanmıyor, ham $MFT attribute
parse ediyor; görev FSCTL_ENUM_USN_DATA istediği için UsnParser seçildi.)

**Lisans/AGPL uyumu:** MIT. Tam uyumlu, kod doğrudan adapte edilebilir.

**Mekanizma (dosya:satır):**
- `UsnParser/Enumeration/MasterFileTableEnumerator.cs:25-67` — `GetData()`:
  `MFT_ENUM_DATA_V0{StartFileReferenceNumber, LowUsn, HighUsn}` doldurulup
  `DeviceIoControl(_volumeRootHandle, FSCTL_ENUM_USN_DATA, ...)` çağrılıyor.
- `FindNextEntry()` satır 69-94: dönen tamponun ilk 8 baytı bir sonraki `StartFileReferenceNumber`
  olarak alınıyor (82), `_record->RecordLength` kadar ilerleyerek döngü kuruluyor (73-74, 86-88).
  `ERROR_HANDLE_EOF` ile tarama biter (55-58).
- Struct'lar: `Native/MFT_ENUM_DATA_V0.cs:10-35`, `Native/USN_RECORD_V2.cs:42-330`.

**USN_RECORD boyut taşımaz:** `USN_RECORD_V2` (42-330) sadece `RecordLength`, version, FRN,
ParentFRN, Usn, TimeStamp, Reason, SourceInfo, SecurityId, FileAttributes, FileName taşır — boyut
alanı yok. `Enumeration/UsnEntry.cs:8-44` de boyut property'si eklemiyor. UsnParser boyutu USN
kaydından hiç almıyor; consumer ayrıca `GetFileAttributesEx`/`FindFirstFile` çağırmak zorunda —
kütüphane bunu çözmüyor.

**Parent/child ağaç:** `UsnJournal.cs:216-303` (`TryGetPath`) — kalıcı ağaç yok, yol tembel
çözülüyor: LRU cache (tanım 22, init 43, kapasite 4096) → yoksa
`NtCreateFile(FILE_OPEN_BY_FILE_ID | FILE_OPEN_FOR_BACKUP_INTENT)` (247-256) →
`NtQueryInformationFile(FileNameInformation)` (264-286).

**Gereken yetki:** `UsnJournal.cs:166-183` (`GetVolumeRootHandle`) — `\\.\D:` köküne
`CreateFile(GENERIC_READ, ...)`. `SeManageVolumePrivilege`/`AdjustTokenPrivileges` çağrısı yok;
`FSCTL_ENUM_USN_DATA` bu ayrıcalığı gerektirmiyor, sadece admin + `GENERIC_READ` yeterli —
`FSCTL_GET_NTFS_VOLUME_DATA`'dan farklı bir gereksinim seviyesi.

**Hardlink/reparse/sparse:** `FileAttributes` ham geliyor (`REPARSE_POINT=0x400`,
`SPARSE_FILE=0x200`, `COMPRESSED=0x800`; `Native/FileFlagsAndAttributes.cs:48,51,57`) ama
`UsnEntry.cs:26`'da bu alan private, sadece `IsFolder`/`IsHidden` açık — genişletmek gerekir.
Hardlink dedup yok; farklı hardlink adları aynı FRN'e sahip ayrı USN kayıtları olarak gelebilir.

**Artımlı güncelleme:** Var — `Enumeration/ChangeJournalEnumerator.cs` +
`ChangeJournalEnumerable.cs`, `FSCTL_READ_USN_JOURNAL`. Journal yoksa `CreateUsnJournal` akışı
(`UsnJournal.cs` ~55-70, `ERROR_JOURNAL_NOT_ACTIVE` yakalama).

**Ölçülen hız:** Yok (yazar Rust portuna — usn-parser-rs — işaret ediyor, rakam vermiyor).

**Tuzaklar:** Silinmiş dosyalar MFT kaydı yeniden kullanılana dek görünmeye devam eder, kütüphane
otomatik filtrelemiyor. İlk tam tarama maliyeti nicelendirilmemiş. Journal taşması
`ERROR_JOURNAL_ENTRY_DELETED` olarak fırlatılıyor, otomatik toparlanma yok.

**DustyBytes'a somut etki:** `NtCreateFile(FILE_OPEN_BY_FILE_ID)` + LRU cache yol çözme deseni
artımlı USN taramasında doğrudan uyarlanabilir. Boyut ve reparse/sparse bayrakları DustyBytes'ın
kendi katmanında ayrıca çözülmeli.

## 4. 0x4Devs/argus — DERİN

C++20 + Qt6, MIT, bugün (2026-09-25) push edilmiş, v0.10.0. (Alternatif farfella/ntfs-cpu-search
— 28 yıldız, 2011 — **lisanssız**: repoda LICENSE dosyası yok, README'de yalnız "Copyright ...
All rights reserved. Contact author for commercial use" var; OSI onaylı değil, AGPL projesine
kod satırı kopyalanamaz. Bu yüzden argus seçildi.)

**Lisans/AGPL uyumu:** MIT. Tam uyumlu.

**Mekanizma (dosya:satır) — FSCTL_ENUM_USN_DATA değil, ham $MFT okuma:**
- `src/core/index.cpp:140-142` — `\\.\C:` `CreateFileW(GENERIC_READ, ...)` ile volume handle.
- Boot sektör: 156-165, `ParseBootSector` (`src/core/ntfs.cpp:7-24`) — `bytes_per_cluster`,
  `bytes_per_mft_record` (negatifse 2^abs(x), 14-18), `mft_byte_offset = mft_lcn * bytes_per_cluster` (20).
- MFT record 0 okunur (168-176), `ApplyFixup` (`ntfs.cpp:26-51`) "FILE" imzası + USA doğrulama.
- Record 0'ın $DATA runlist'i `DecodeRunlist` (`ntfs.cpp:53-93`, sparse run destekli) ile çözülür —
  tüm $MFT'nin fiziksel disk yerleşimi elde edilir. `FSCTL_GET_NTFS_VOLUME_DATA` hiç kullanılmıyor.
- `index.cpp:206-264` — 8 MB chunk'larla ham okuma, her record "FILE" imzası + fixup + in-use
  bayrağı + `base_record==0` (extension record atlanır, 236-237) kontrolünden geçer.

**Bellek içi indeks:** `entries_` vektörü, `name_pool_` (32 MB başlangıç, tüm adlar tek UTF-16
pool'da), `mft_to_idx_` (O(1) ters bakış). `ExtractBestName` (37-78) namespace önceliğiyle
(Win32AndDos=0 > Win32=1 > Posix=2 > DOS=3, 53-59) en iyi adı seçer. `full_path()` (96-123)
parent_mft zincirini root'a (kMftId=5) kadar tembel yürür, kalıcı ağaç saklanmaz.

**Gereken yetki:** Kodda açık `AdjustTokenPrivileges` yok ama README "Run as administrator (raw
NTFS access requires elevated privileges)" diyor; `res/argus.exe.manifest` muhtemelen
`requireAdministrator`.

**Boyut/hardlink/reparse/sparse:** Boyut `$FILE_NAME`'in `data_size` alanından (71,
`fn->data_size`) — ayrı bir `FindFirstFile` çağrısı yok, çünkü ham MFT record okunuyor. Hardlink:
`ExtractBestName` yalnız "en iyi" tek adı seçer (60) → her MFT kaydı index'te tek Entry — bu
DustyBytes'ın "hardlink bir kez sayılır" kuralına doğal uyuyor, ama diğer hardlink adları/konumları
ana index'e hiç girmiyor (yalnız `ReadMftDetails`, 527-591, on-demand ile görülür, gerçek
`hard_link_count` 558). Reparse point hiç özel işlenmiyor — bayrak ana taramada çıkarılmıyor, junction
hedefi okunmuyor. Sparse/compressed bayrakları index'e taşınmıyor (yalnız DIRECTORY biti, 70,241).

**Artımlı güncelleme:** Var — MFT taramasından sonra `FSCTL_QUERY_USN_JOURNAL`/gerekirse
`FSCTL_CREATE_USN_JOURNAL` (276-293, 32MB/8MB). `ApplyUsnChanges()` (305-402)
`FSCTL_READ_USN_JOURNAL` ile create/delete/rename/close günceller. Bounded loop (max 16, 328-329)
sonsuz bloklanmayı engeller. Taşma `rolled_over` bayrağıyla bildirilir (335) — DustyBytes'ın
"MFT + sonraki taramalar USN artımlı" akışına birebir örnek.

**Ölçülen hız:** README "indexes millions of files ... in seconds", "<1s + USN catch-up" iddiaları
var ama metodoloji/donanım/rakam kaynağı yok — kanıtsız.

**Tuzaklar:** Hardlink alternatif adları ana index'te kayboluyor. Reparse ayrımı yok. Sparse/
compressed bayrakları taşınmıyor. Cache dosyası (.aix) versiyon uyuşmazlığında sessizce reddediliyor
(`LoadFrom`, 481).

**DustyBytes'a somut etki:** Boot sektör → $DATA runlist → fixup → 8MB chunk okuma zinciri
"Hızlı kip" için satır satır izlenebilir MIT referans. Reparse/hardlink/sparse/cloud-placeholder
ayrımı hiçbir incelenen depoda plan seviyesinde hazır değil — DustyBytes'ta sıfırdan tasarlanmalı.

## 5. sharkdp/fd

MIT OR Apache-2.0, AGPL ile tam uyumlu. 44.533 yıldız, son push 2026-09-24.

**Mekanizma:** fd kendi walker'ını yazmıyor, `ignore = "0.4.28"` crate'ini kullanıyor (jwalk
DEĞİL, Cargo.toml doğrulandı). `src/walk.rs`: `WalkParallel`/`WalkState`, sonuçlar
`crossbeam-channel` üzerinden batch akıyor (`BatchSender`, `MAX_BUFFER_LENGTH=1000`). Alıcı 100ms
Buffering modunda toplar, hızlı biterse sıralı basar, uzarsa Streaming moduna geçip anlık yazar.

**Gereken yetki:** Yok. **Hardlink/reparse:** fd düzeyinde değil, `ignore` crate'inde (aşağıda).

**Ölçülen hız:** README "Benchmark" — ~750.000 alt dizin, ~4M dosya, hyperfine:
`find -iregex` 19.9s, `find -iname` 11.2s, `fd` 854.8ms. Kaynak: sharkdp/fd-benchmarks.

**Tuzaklar:** Asıl paralel/ignore mantığı fd'de değil `ignore` crate'inde.

**DustyBytes'a somut etki:** batch+kanal+zaman-eşikli buffer→stream geçişi doğrudan uygulanabilir
desen (Bölüm 1, sonuç akışı donmasın gereksinimi).

### ignore crate (BurntSushi/ripgrep altında, fd'nin gerçek motoru)

Unlicense OR MIT, AGPL ile tam uyumlu. ripgrep deposu: 68.583 yıldız, aktif.

**Mekanizma (`crates/ignore/src/walk.rs`):** Paralel modda `crossbeam_deque::{Stealer, Worker}` —
work-stealing deque tabanlı elle yazılmış thread havuzu (rayon değil). Tek-thread modda `walkdir`'i
sarmalıyor. `WalkState` enum (Continue/Skip/Quit) callback ile erken durdurma/dal atlama sağlıyor.

**Symlink:** `follow_links: bool`, varsayılan `false` (satır 567), `-L` ile açılır. **Hardlink/loop
koruması:** `same_file::Handle` (same-file crate) — dosya kimliğine göre tekilleştirme.

**DustyBytes'a somut etki:** `WalkState` + crossbeam-deque work-stealing + same-file (dosya kimliği)
üçlüsü doğrudan model. C# karşılığı: `Channel<T>` + .NET ThreadPool work-stealing +
`GetFileInformationByHandle`/`FILE_ID_INFO` ile hardlink/reparse tekilleştirme.

## 6. BurntSushi/walkdir ve jwalk

walkdir: Unlicense, 1.546 yıldız, son push 2026-06-23. jwalk: MIT, 288 yıldız, son push 2026-08-05.
İkisi de AGPL uyumlu.

**ÖNEMLİ — jwalk DEPRECATED:** Cargo.toml `description = "Use \`dua-core\` instead"`,
`[badges] maintenance = { status = "deprecated" }`. Yeni projede referans alınmamalı,
`dua-core`'a bakılmalı.

**walkdir mekanizması:** tek iş parçacıklı, klasik DFS Iterator, `std::fs::read_dir` lazy
zincirleme, paralellik yok. **jwalk mekanizması:** `rayon = "1.5"` + `crossbeam = "0.8"` (gerçek
work-stealing rayon thread pool), sonuç sıralaması opsiyonel/varsayılan garanti değil.

**Reparse/symlink:** ikisi de varsayılan `follow_links(false)`. Hardlink/loop tespiti kendi
başlarına yok, `same-file` crate dışarıdan eklenir. **Gereken yetki:** yok.

**DustyBytes'a somut etki:** work-stealing paralel dizin kuyruğu deseni C#'a taşınabilir — her
dizin bir iş öğesi olarak `Channel`'a yazılır, sabit boyutlu Task havuzu (ProcessorCount kadar)
`FindFirstFileEx(FIND_FIRST_EX_LARGE_FETCH)` ile tarar, alt dizinleri kanala geri yazar, sonuçları
batch halinde ayrı sonuç kanalına yollar. jwalk'ın kendisi kullanılmamalı (deprecated), yalnız
deseni referans.

## 7. libyal/libfsntfs

(Microsoft Windows-classic-samples deposunda USN/ChangeJournal/FindFirstFileEx örneği aranmış,
154 klasör tam taranmış, uygun örnek **bulunamamış** — en yakınlar ProjectedFileSystem,
ScanRestorableFiles, StorageManagement, VShadowVolumeShadowCopy, hiçbiri uygun değil. Bu yüzden
libfsntfs incelendi.) 238 yıldız, son push 2026-09-23.

**Lisans/AGPL uyumu:** LGPL-3.0 (dosya başlıklarında doğrulandı). LGPL-3.0, AGPL-3.0-or-later ile
**birlikte kullanılabilir** — ayrı kütüphane/link ilişkisi olarak, kaynak karışımı değil.

**Mekanizma:** $MFT ham ayrıştırma, dosya bazlı net bölünmüş: `libfsntfs_mft.c` /
`_mft_entry.c` / `_mft_entry_header.c` (MFT girdileri), `_mft_attribute.c` / `_attribute_list.c`
($FILE_NAME, $STANDARD_INFORMATION), `_reparse_point_attribute.c` / `_reparse_point_values.c`
($REPARSE_POINT — junction/symlink tespiti ayrı dosyada), `_volume.c` / `_volume_header.c` (birim
düzeyi $MFT erişimi). Offline/raw disk parser, canlı dosya sistemi API'si değil.

**Gereken yetki:** Ham disk erişimi için (`\\.\C:`) **yönetici zorunlu** — DustyBytes'ın
varsayılan "yönetici istemez" hedefiyle doğrudan çelişiyor.

**Artımlı güncelleme:** Yok. $MFT'i statik ayrıştırır; gerçek USN Change Journal için ayrı API
gerekir (`FSCTL_QUERY_USN_JOURNAL`/`FSCTL_READ_USN_JOURNAL`), libfsntfs bunu kapsamıyor.

**Ölçülen hız:** Belirtilmiyor.

**Tuzaklar:** (1) yönetici gereksinimi varsayılan yolla çelişiyor, (2) ham $MFT ayrıştırma
sıkıştırma/şifreleme/ADS gibi özel durumlara kırılgan, (3) C kütüphanesi — CsWin32 P/Invoke
modeliyle değil, harici native derleme/binding ile entegre edilmeli.

**DustyBytes'a somut etki:** Varsayılan tarama yoluna (FindFirstFileEx, yönetici istemez)
uymuyor — olası isteğe bağlı "gelişmiş tarama" (yönetici + ham $MFT, daha hızlı tam disk) için
ayrı opsiyonel motor olabilir, varsayılan değil. Gerçek USN Journal artımlı izleme için Microsoft
örnek deposunda hazır kod yok; `FSCTL_QUERY_USN_JOURNAL`/`FSCTL_READ_USN_JOURNAL` P/Invoke'ları
(CsWin32 ile üretilebilir, madde 2'deki spike'a bağlı) MSDN'den elle çıkarılmalı.

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 1. Tarama Motoru — varsayılan yol | Paralel dizin gezinmede work-stealing deque + `WalkState` erken-durdurma deseni, batch+kanal+zaman-eşikli buffer→stream geçişi uygulanabilir | sharkdp/fd, ignore crate |
| 1. Tarama Motoru — varsayılan yol | jwalk yerine desen olarak `rayon`/work-stealing referans alınmalı, jwalk'ın kendisi kullanılmamalı (deprecated) | BurntSushi/jwalk, walkdir |
| 1. Tarama Motoru — hardlink/reparse tekilleştirme | Dosya kimliğiyle (FRN veya `FILE_ID_INFO`) tekilleştirme deseni; hiçbir incelenen araç hardlink+reparse+sparse+cloud-placeholder ayrımını plan seviyesinde hazır vermiyor, sıfırdan tasarlanmalı | same-file crate (ignore/walkdir), omerbenamram/mft, 0x4Devs/argus, wangfu91/UsnParser |
| 1. Tarama Motoru — bulut yer tutucu ayrımı | `RECALL_ON_DATA_ACCESS`/`OFFLINE`/`PINNED` bayrakları `FileAttributeFlags` enum modeliyle doğrulandı, C#'ta aynı bit maskeleri kullanılabilir | omerbenamram/mft |
| A6 Hızlı Tarama — MFT doğrudan okuma | Boot sektör → $DATA runlist çözümü → fixup → chunk'lı okuma zinciri algoritma referansı; fixup + resident/non-resident + boyut offsetleri C#'a taşınabilir | 0x4Devs/argus, omerbenamram/mft |
| A6 Hızlı Tarama — USN artımlı güncelleme | `FSCTL_ENUM_USN_DATA` ilk tarama + `FSCTL_READ_USN_JOURNAL` artımlı akışı, journal taşması (`ERROR_JOURNAL_ENTRY_DELETED`) yakalayıp tam yeniden taramaya düşme deseni | wangfu91/UsnParser, 0x4Devs/argus |
| A6 Hızlı Tarama — dosya boyutu kaynağı | USN_RECORD boyut taşımaz; boyut ya ayrı `GetFileAttributesEx` çağrısıyla (USN yolunda) ya da $FILE_NAME attribute'unun `data_size` alanından (ham MFT yolunda) alınmalı — plana açık not düşülmeli | wangfu91/UsnParser, 0x4Devs/argus |
| A6 Hızlı Tarama — yetki modeli | `FSCTL_ENUM_USN_DATA` yalnız admin + `GENERIC_READ` ister (`SeManageVolumePrivilege` gerekmez); ham $MFT/`FSCTL_GET_NTFS_VOLUME_DATA` yolu daha ağır yetki gerektirebilir, ikisi ayrı değerlendirilmeli | wangfu91/UsnParser, libyal/libfsntfs |
| A6 Hızlı Tarama — yol çözme | Parent FRN'den yol kurmak için `NtCreateFile(FILE_OPEN_BY_FILE_ID)` + LRU cache deseni | wangfu91/UsnParser |
| A6 Hızlı Tarama — CsWin32 entegrasyonu | `DeviceIoControl` üretimi doğrulandı, ama `FSCTL_ENUM_USN_DATA`/`FSCTL_GET_NTFS_VOLUME_DATA`/ilgili struct'ların üretilebilirliği doğrulanamadı — A6 başında küçük bir spike gerekli | microsoft/CsWin32 |
| A6 Hızlı Tarama — kapsam sınırı | Ham $MFT okuma yolu (libfsntfs deseni) yönetici zorunlu kılıyor; DustyBytes'ın varsayılan "yönetici istemez" hedefiyle çelişiyor, yalnız isteğe bağlı gelişmiş modda düşünülmeli | libyal/libfsntfs |
