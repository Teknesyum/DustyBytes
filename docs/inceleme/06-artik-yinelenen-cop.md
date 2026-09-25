# Geliştirici Artığı, Yinelenen Dosya, Güvenli Silme / Çöp İncelemesi

Kapsam: DustyBytes Bölüm 3 (Birimler — Toplu Teklif) ve Bölüm 5 (Güvenli Silme ve Karantina)
için yedi açık kaynak deponun incelenmesi. `gh api` / `gh repo view` ile README, lisans,
dizin ağacı ve kaynak dosyaları okunmuştur; "DERİN" işaretli depolarda asıl mekanizma
dosya:satır ile anlatılmıştır.

**Depo Adı Düzeltmesi (madde 6):** `andreafrancia/trash-cli` yalnız freedesktop.org
çöp kutusunu hedefliyor, README'sinde Windows'a dair hiçbir şey yok; `nivekuil/rip`
(GPLv3) kendi "graveyard" mekanizmasını kullanıyor, Windows Geri Dönüşüm Kutusu API'sine
dokunmuyor. Windows Recycle Bin'i gerçekten `IFileOperation` ile kullanan depo
**Byron/trash-rs** (MIT) — doğru referans budur.

**Madde 7 için bulunan depolar:** IFileOperation/FOF_ALLOWUNDO için **dahall/Vanara**
(MIT, 2097 yıldız), Restart Manager için **VerifyTests/Verify** içindeki
`src/Verify/RestartManager.cs` (MIT, 3467 yıldız).

---

## 1. voidcosmos/npkill (DERİN)

**Lisans:** MIT (`LICENSE`). AGPL-3.0 ile tam uyumlu.

**Tarama:** `src/core/services/files/files.worker.ts` içindeki `FileWalker` sınıfı,
`worker_threads` üzerinde `opendir`/`dir.read()` ile ağacı gezer (`analizeDir`, ~satır
150-165); hedef klasör adı `isTargetFolder` (~satır 250) ile eşleşince işaretlenir.
`GLOBAL_IGNORE` seti sembolik linkleri ve bazı klasörleri atlar; iş kuyruğu `MAX_PROCS`
ile paralel sınırlanır.

**Boyut hesabı:** Aynı worker'daki `runGetFolderSizeChild` (~satır 195-235), 100'lük
parçalar halinde `Promise.all` ile paralel `lstat` çağırır ve `stats.blocks * 512`
toplar — gerçek disk blok kullanımı, `stats.size` değil.

**Silme:** `unix-files.service.ts:14-23` → `execFile('rm', ['-rf', path])`;
`windows-files.service.ts:11-14` → `fs.rm(path, {recursive:true, force:true})`.
**Geri alınamaz**, çöp kutusuna gitmiyor. Silme öncesi `files.service.ts` içindeki
`isDangerous()` (~satır 78-190) HOME'daki `.config`, `.cache`, Windows `AppData/Roaming`,
`Program Files` gibi yolları "hassas" işaretleyip ayrı uyarı gösteriyor.

**Yanlış silme şikayeti:** Issue #60, "Allow moving folders to trash rather than
completely deleting them" (açık).

**Plana etkisi:** `isDangerous()`'daki yol-öneki tabanlı hassasiyet haritası, Bölüm 5
"korumalı liste" mantığı için doğrudan örnek desen. Blok tabanlı boyut hesabı
(`stats.blocks*512`) Bölüm 3 puanlamasında diskte gerçekten kaplanan alanı yansıtmak
için tercih edilebilir.

## 2. tbillington/kondo (DERİN)

**Lisans:** MIT (`LICENSE`).

**Tespit mekanizması:** `kondo-lib/src/lib.rs` — işaret-dosyası → `ProjectType` eşlemesi
satır 401-441 (`match` bloğu), silinebilir klasör listeleri satır 35-94, `clean()`
fonksiyonu satır 317-328 → doğrudan `fs::remove_dir_all(&artifact_dir)` (kalıcı, çöp
kutusuna gitmiyor).

| İşaret Dosyası (satır) | Silinebilir Klasör (satır) | Ekosistem |
|---|---|---|
| `Cargo.toml` (9) | `target`, `.xwin-cache` (35) | Rust/Cargo |
| `package.json` (10) | `node_modules`, `.angular` (36); React Native'de ayrıca `android/build`, `android/.gradle`, `ios/build`, `ios/DerivedData`, `ios/Pods`, `.expo`, `.metro` (37-45) | Node.js |
| `Assembly-CSharp.csproj` (11) | `Library`, `Temp`, `Obj`, `Logs`, `MemoryCaptures`, `Build`, `Builds` (47-54) | Unity |
| `stack.yaml` (12) | `.stack-work` (56) | Haskell/Stack |
| `cabal.project` (13) | `dist-newstyle` (57) | Haskell/Cabal |
| `build.sbt` (14) | `target`, `project/target` (58) | Scala/SBT |
| `pom.xml` (15) | `target` (59) | Maven |
| `build.gradle` / `.kts` (16-17) | `build`, `.gradle` (60) | Gradle |
| `CMakeLists.txt` (18) | `build`, `cmake-build-debug`, `cmake-build-release` (61) | CMake |
| `*.uproject` (19) | `Binaries`, `Build`, `Saved` +2 (62-67) | Unreal Engine |
| `*.ipynb` (20) | `.ipynb_checkpoints` (69) | Jupyter |
| `*.py` (21) | `.mypy_cache`, `.nox`, `.pytest_cache` +4 (70-77) | Python |
| `pixi.toml` (22) | `.pixi` (79) | Pixi |
| `composer.json` (23) | `vendor` (80) | PHP/Composer |
| `pubspec.yaml` (24) | `build`, `.dart_tool`, `linux/flutter/ephemeral` +1 (81-86) | Dart/Flutter/Pub |
| `mix.exs` (25) | `_build`, `.elixir-tools`, `.elixir_ls`, `.lexical` (87) | Elixir |
| `Package.swift` (26) | `.build`, `.swiftpm` (88) | Swift |
| `build.zig` (27) | `zig-cache`, `.zig-cache`, `zig-out` (89) | Zig |
| `project.godot` (28) | `.godot` (90) | Godot 4 |
| `*.csproj`/`*.fsproj` (29-30) | `bin`, `obj` (91) | .NET |
| `.terraform.lock.hcl` (31) | `.terraform` (93) | Terraform |
| `turbo.json` (32) | `.turbo` (92) | Turborepo |
| `Podfile` (33) | `Pods` (94) | CocoaPods |

**Yanlış silme şikayeti:** Issue #191, "Do not delete things in `Library/Application
Support`" (açık, macOS yanlış-pozitif); Issue #149, "Misidentification of Unity
Projects" (yanlış tür tespiti); Issue #96, "Support moving artifacts to system trash"
(açık — npkill #60 ile aynı talep).

**Plana etkisi:** Bölüm 3 "Geliştirici artığı" satırı için bu 20 ekosistemlik tam liste
doğrudan genişletilebilir bir kural seti (özellikle Gradle, CMake, Unreal, Godot, Zig,
Turborepo şu an planda yok). Her iki aracın da kalıcı silmeyi tercih etmesi ve buna gelen
şikayetler, geliştirici artığında bile karantina + koruma listesinin doğru yön olduğunu
doğruluyor.

## 3. pkolaczk/fclones (DERİN)

**Lisans:** MIT. AGPL-3.0-or-later ile tek yönlü uyumlu (MIT kod AGPL projeye girebilir).

**Kademeli algoritma** (`fclones/src/group.rs`, `group_files()` satır 1221):
1. Boyuta göre gruplama — `group_by_size()` (satır 793)
2. Ön ek (prefix) hash — `group_by_prefix()` (satır 1017)
3. Son ek (suffix) hash — `group_by_suffix()` (satır 1075), yalnız
   `file_len >= suffix_threshold && unique_count() > 1` olan gruplara uygulanır (satır 1088)
4. Tam içerik hash — `group_by_contents()` (satır 1118), `--skip-content-hash` ile atlanabilir

**Hash algoritması:** `fclones/src/hasher.rs`, `enum HashFn` (satır 26) — varsayılan
**MetroHash128**, opsiyonel `xxhash3` ve `blake3`. Kriptografik değil, hız odaklı.

**Yer açma:** `enum DedupeOp` (`fclones/src/dedupe.rs:32-42`): `Remove`, `Move`,
`SymbolicLink`, `HardLink`, `RefLink`. Hardlink düz `std::fs::hard_link()` (satır 166),
platform ayrımı yok — NTFS'te `CreateHardLink` üzerinden çalışır ve fiziksel olarak
aynı birimi gerektirir. Reflink Windows'ta desteklenmiyor (yalnız Linux `FICLONE`
ioctl'i ele alınmış).

**Yanlış eşleştirme şikayeti:** Issue #84, "Detect changes after `fclones group`" (açık)
— gruplama ile dedupe (hardlink/reflink) arasında dosya değişirse TOCTOU riski, sessiz
veri bozulması.

**Plana etkisi:** 4 kademeli algoritma + MetroHash, Bölüm 3'teki ertelenmiş yinelenen
dosya özelliği için doğrudan şablon. Hardlink'in volume sınırını aşamaması, planın
"birimler arası kopyala-sil asla" kuralını fiziksel olarak doğruluyor. Issue #84,
DustyBytes'ın silme/hardlink işlemi öncesi dosyanın tarama anındaki haliyle hâlâ eşleştiğini
(boyut/mtime ya da hash) yeniden doğrulaması gerektiğini gösteriyor.

## 4. arsenetar/dupeguru

**Lisans:** GPL-3.0. AGPL-3.0 ile tek yönlü uyumlu.

**Fotoğraf/film benzerliği:** `core/pe/` (picture engine) — her görsel 15x15 kareye
bölünüp (`BLOCK_COUNT_PER_SIDE`, `core/pe/matchblock.py:34`) her bloğun ortalama RGB'si
çıkarılır (`_block` C uzantısı), bloklar arası fark `avgdiff()` ile karşılaştırılır —
DCT/pHash değil, kendi blok-tabanlı yaklaşımı. `matchblock.py` çok işlemli chunk'larla
ölçekleniyor; sonuçlar `core/pe/cache_sqlite.py` ile önbelleğe alınıyor.

**Plana etkisi:** Planın "Ertelenenler" kısmındaki fotoğraf/film benzerliği özelliği
için referans — blok-tabanlı ortalama-renk karşılaştırma C#/SkiaSharp ile kolayca
yeniden yazılabilir, ayrı bir pHash kütüphanesi gerekmiyor.

## 5. sahib/rmlint

**Lisans:** GPLv3 (`COPYING`).

Boyuta göre gruplama, ardından artan doğrulukta hash aşamaları (xxhash → sha3/blake
ailesi), `-p/--paranoid` ile byte-byte karşılaştırma. **Kritik güvenlik önlemi: rmlint
hiçbir zaman kendi başına silmez** — varsayılan çıktı bir `rmlint.sh` betiği üretir,
kullanıcı bunu gözden geçirip kendisi çalıştırır. `-k` (tercih edilen dizinleri koru),
`--keep-hardlinked` gibi koruma bayrakları var.

**Plana etkisi:** "Üret, göster, onaylat, sonra uygula" akışı Bölüm 5'teki raf kuralına
zıt bir tasarım tercihi olarak dikkat çekici — DustyBytes'ın "geri alınabilir eylem
onaylatılmaz" ilkesi, rmlint'in "kalıcı eylem her zaman gözden geçirilir" ilkesiyle
birlikte okunduğunda, planın geri alınamaz yinelenen-dosya silmelerinde de rmlint tarzı
bir "önce göster" adımını değerlendirmesi gerektiğini gösteriyor.

## 6. Byron/trash-rs (DERİN) — düzeltilmiş depo

**Lisans:** MIT (`LICENSE.txt`). AGPL-3.0 ile tam uyumlu.

**Win32 çağrıları** (`src/windows.rs`): `IFileOperation` COM arayüzü (SHFileOperation
değil, modern API). `delete_specified_canonicalized` fonksiyonu: `CoCreateInstance`
→ `SetOperationFlags(FOF_NO_UI | FOF_ALLOWUNDO | FOF_WANTNUKEWARNING)` → her dosya için
`SHCreateItemFromParsingName` + `pfo.DeleteItem(&shi, None)` → `pfo.PerformOperations()`
→ `pfo.GetAnyOperationsAborted()` ile kontrol.

**Geri yükleme bilgisi:** `list()` fonksiyonu `SHGetKnownFolderItem(&FOLDERID_RecycleBinFolder, ...)`
ile Geri Dönüşüm Kutusu'nu `IEnumShellItems` üzerinden gezer; her öğe için
`SCID_ORIGINAL_LOCATION` ve `SCID_DATE_DELETED` (`IShellItem2::GetProperty`) okunur —
orijinal yol ve silinme tarihi Recycle Bin'in kendi meta verisinden geliyor, ayrı manifest
gerekmiyor. `restore_all()` `IFileOperation::MoveItem` ile geri taşıyor; hedef doluysa
`Error::RestoreCollision` döner.

**Uzun yol / UNC:** `to_shell_parsing_name()` verbatim önekli yolları (`\\?\C:\...`,
`\\?\UNC\host\share\...`) shell'in kabul ettiği biçime çeviriyor; testler 260 karakterden
uzun yolları da kapsıyor.

**Windows'a özgü sorunlar:** Issue #55 (paylaşılan klasörde silme başarısız — verbatim/UNC
path sorunu, kapatıldı), #70 (boş olmayan klasör sessizce silinemiyor, kapatıldı), #104
(dosya başka süreçte açıkken hata dönmüyor, kapatıldı), #96 (Windows hata kodu eşleme,
açık).

**Plana etkisi:** Plan zaten "yazılabilir kök yoksa `IFileOperation` + `FOF_ALLOWUNDO`"
diyor (Bölüm 5) — trash-rs bunun kanıtlanmış referans uygulaması. C#'a taşıma CsWin32 ile
(plan zaten kullanıyor) tip güvenli olur. `to_shell_parsing_name`'deki verbatim-yol
düzeltmesi doğrudan uyarlanmalı. `RestoreCollision` kontrolü Bölüm 5'teki "orijinal yol
doluysa çakışma ekranı" maddesiyle birebir örtüşüyor; trash-rs'in check-then-move deseni
race condition'a açık — DustyBytes bunu SQLite manifestle daha güvenli yapabilir.

## 7. IFileOperation ve Restart Manager — C# örnekleri

**dahall/Vanara** (MIT, 2097 yıldız, güncel bakım) — IFileOperation:
- Ham COM arayüzü: `PInvoke/Shell32/ShObjIdl.IFileOperation.cs:167`.
- Yüksek seviye sarmalayıcı: `Windows.Shell.Common/ShellFileOperations.cs` — varsayılan
  bayrak `OperationFlags.AllowUndo | OperationFlags.NoConfirmMkDir` (satır 12); `Options`
  değişince `op.SetOperationFlags(...)` çağrılır (satır 97-100).
- `Windows.Shell.Common/RecycleBin.cs`: `DeleteToRecycleBin(...)` (satır 29, 34) →
  `GetDeleteOpFlags(hideUI)` (satır 103-111) — Windows 8+ için `AddUndoRecord |
  RecycleOnDelete` FOFX_ bayrakları da ekleniyor. Hata yönetimi `HRESULT.ThrowIfFailed()`
  (satır 76, 90).

**VerifyTests/Verify** (MIT, 3467 yıldız) — `src/Verify/RestartManager.cs` (146 satır),
Restart Manager:
- `GetProcessesLockingFile(string path)` (satır 73): `RmStartSession` (78) →
  `RmRegisterResources` (87) → iki geçişli `RmGetList` (önce boyut sorgusu satır 96,
  sonra gerçek liste satır 109) → `finally`'de `RmEndSession` (141).
- Her `RM_PROCESS_INFO.Process.dwProcessId` için `Process.GetProcessById` çağrılıp
  listeye eklenir; kendi sürecinin PID'i atlanır.

Her iki depo MIT — AGPL-3.0 ile tam uyumlu, atıf yeterli; kod kopyalamak yerine NuGet
paketi (`Vanara.Windows.Shell.Common`) olarak referans almak da mümkün.

**Plana etkisi:** Bölüm 5, "yazılabilir kök yoksa `IFileOperation` + `FOF_ALLOWUNDO`" ve
"açık dosya: Restart Manager tutan süreci adıyla gösterir" maddelerinin ikisi için de
doğrudan uygulanabilir referans kod sağlıyor. FOF_ALLOWUNDO/FOFX_ADDUNDORECORD ile Geri
Dönüşüm Kutusu'na taşıma OS seviyesinde zaten geri alınabilir olduğundan, Bölüm 5'teki
"raf kuralı: geri alınabilir eylem onaylatılmaz, yapılır ve Geri al sunulur" ilkesiyle
mimari tam örtüşüyor.

---

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| Bölüm 3 — Birimler | Geliştirici artığı kural setine Gradle, CMake, Unreal, Godot, Zig, Turborepo, Terraform, Elixir, Swift, Zig, Pixi, Dart/Flutter eklenmeli | tbillington/kondo |
| Bölüm 3 — Birimler | Klasör boyutu hesabında diskte gerçekten kaplanan alan (blok tabanlı) tercih edilmeli | voidcosmos/npkill |
| Bölüm 5 — Güvenli Silme | Korumalı liste, yol-öneki tabanlı hassasiyet haritası (AppData/Roaming, Program Files, .config) ile genişletilmeli | voidcosmos/npkill |
| Bölüm 5 — Güvenli Silme | `IFileOperation` + `FOF_ALLOWUNDO`/FOFX_ bayrakları CsWin32/Vanara desenine göre uygulanmalı | Byron/trash-rs, dahall/Vanara |
| Bölüm 5 — Güvenli Silme | Geri yükleme bilgisi Recycle Bin'in kendi `SCID_ORIGINAL_LOCATION`/`SCID_DATE_DELETED` özelliklerinden okunabilir; SQLite manifest ile çakışma kontrolü (RestoreCollision) güçlendirilmeli | Byron/trash-rs |
| Bölüm 5 — Güvenli Silme | Uzun yol/UNC (verbatim önek) dönüşümü ayrıca ele alınmalı | Byron/trash-rs |
| Bölüm 5 — Güvenli Silme | Kilitli dosya tespiti için Restart Manager (RmStartSession/RmRegisterResources/RmGetList) akışı eklenmeli | VerifyTests/Verify |
| Ertelenenler — Yinelenen Dosya | Boyut → ön ek hash → son ek hash → tam hash kademeli algoritması, MetroHash ile | pkolaczk/fclones |
| Ertelenenler — Yinelenen Dosya | Hardlink ile yer açma, TOCTOU kontrolü (işlem anında dosyanın hâlâ eşleştiğini doğrulama) eklenerek | pkolaczk/fclones |
| Ertelenenler — Fotoğraf/Film Benzerliği | Blok-tabanlı ortalama-renk karşılaştırma (SkiaSharp ile uyarlanabilir) | arsenetar/dupeguru |
