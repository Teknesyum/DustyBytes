# DustyBytes Planı

Diski tarar, büyük ve az kullanılanı üste koyar, amaca göre toplar ("şu oyun, şu film, şu
klasör") ve tek tıkla yer açar. Program kaldırınca kalıntı bırakmaz. Format atmaya gerek kalmaz.

**Sürüm 2 — inceleme sonrası revizyon (2026-09-25).** Sürüm 1: `trash/plan-v1.md`.
Değişen her madde sonunda köşeli parantezle kaynak deposunu taşır.

Kaynaklar: 61 açık kaynak depo, yedi raporda (bkz. İnceleme Dizini); özel raf (`kimlik`, `calisma`, `depo`, `araclar`, `lisans`, `readme-protokolu`,
`ui`, `ui-duzeni`, `ui-denetim`, `kabuk-standardi`, `README-kabuk-standardi`,
`guncelleme-paneli`, `bilesen-surumleri`, `yazim`) ve fable görüşü
[001](danisma/001-fable-dustybytes-plan.md).

## Kararlar

| Konu | Karar | Gerekçe |
|---|---|---|
| Yığın | Avalonia 11 + .NET 10 LTS, C# | Raf: ağır yerel iş Avalonia. Kabuk standardı ve testleri Avalonia için yazılı. |
| Windows API | CsWin32 kaynak üreteci | MFT, USN, Restart Manager, MSI, kayıt defteri tip güvenli P/Invoke. FSCTL yapılarının üretildiği A0'da deneme derlemesiyle doğrulanır. [CsWin32] |
| Hazır kod | Yalnız MIT, Apache, ISC, BSD, Unlicense, LGPL (paket olarak) ve GPL-3.0 kaynak alınır; GPL-2.0-only, MSR-SSLA ve lisanssız depo yalnız fikir | Lisans tuzağı üç depoda çıktı. [qdirstat, menees/Treemap, ntfs-cpu-search] |
| Süreç | Aynı exe iki kipte: arayüz (yetkisiz) + `--worker` (yönetici, gerektiğinde) | UIPI sürükle-bırakı bozmaz, çökme yalıtılır, güven sınırı worker'da. |
| Köprü | Named pipe, yalnız aynı kullanıcı SID'i, karşı PID doğrulanır | Hizmet kurulmaz; kalıcı saldırı yüzeyi yok. |
| Dizin | SQLite önbelleği, sonuçlar worker'dan akışla | Açılışta önbellekten anında gösterir, tarama arkadan tazeler. |
| Silme | Doğrudan silme yok; birim başına karantina ya da kategori politikası | Güven ilk geri getirilemeyen silmede biter. |
| Lisans | AGPL-3.0-or-later, `scaffold.js license` ile | Raf: lisans. |
| Dağıtım | Kur penceresi + güncelleme rozeti şablondan, kod imzası | İmzasız exe Defender/SmartScreen'e takılır. |

## Mimari

```
DustyBytes.exe (arayüz, yetkisiz)
 ├─ Avalonia görünümleri ── ViewModel ── Durum nesnesi (kaynak, hedef, yuzde, tavan, adim, log, durum)
 ├─ SQLite önbellek (okuma)
 └─ named pipe ──► DustyBytes.exe --worker (yönetici, istek üzerine UAC bir kez)
                    ├─ Tarayıcı: FindFirstFileEx LARGE_FETCH paralel │ MFT + USN (hızlı kip)
                    ├─ Sinyal toplayıcılar: Prefetch, UserAssist, Steam, Epic, GOG, oynatıcılar
                    ├─ Birim çıkarıcılar (oyun, film, program, geliştirici artığı…)
                    ├─ Korumalı yol denetimi ◄── her sil/taşı isteği buradan geçer
                    ├─ Karantina yöneticisi
                    └─ Kaldırıcı + kalıntı tarayıcı
```

Tek örnek: adlandırılmış mutex ve aktivasyon pipe'ı [Polymerium]. Worker pipe'ı `PipeSecurity`
ile yalnız aynı SID'e açılır, `GetNamedPipeClientProcessId` ile karşı süreç doğrulanır; bunu
incelenen hiçbir depo yapmıyor, kendimiz yazıyoruz.

Arayüz hiçbir dosyaya dokunmaz; yalnız istek gönderir. Worker arayüze güvenmez, her isteği
korumalı listeyle yeniden denetler.

## Proje Düzeni

```
src/DustyBytes.App        Avalonia arayüz, görünümler, tema (Theme.axaml token'dan)
src/DustyBytes.Core       Birim modeli, puanlama, korumalı liste, DTO'lar
src/DustyBytes.Scan       Tarayıcı, MFT/USN, SQLite dizin
src/DustyBytes.Signals    Kullanım sinyali toplayıcıları
src/DustyBytes.Units      Birim çıkarıcılar
src/DustyBytes.Clean      Karantina, temizlik kuralları, kaldırıcı, kalıntı
src/DustyBytes.Worker     Pipe sunucusu, --worker girişi
tests/DustyBytes.Tests    Birim testleri + KabukStandardiTests
tests/fixtures            Sahte disk ağaçları, örnek .pf, .acf, Uninstall kayıtları
docs/                     plan, yol haritası, tasarım, danışma, ui-denetim
```

## 1. Tarama Motoru

- **Varsayılan yol:** `FindFirstFileEx` + `FIND_FIRST_EX_LARGE_FETCH`, dizin başına paralel.
  Yönetici istemez. Dizinler bir `Channel` kuyruğuna iş olarak yazılır, eşzamanlılık
  `2 × çekirdek` ile semaforla kısılır; sınırsız iş parçacığı açılmaz. [gdu, jwalk, ignore]
- Sonuç ana tarafa toplu gider (1000'lik paket); tarama kısa sürerse sıralı gösterilir,
  uzarsa anlık akışa geçilir. [fd]
- Ağ ve UNC sürücüsünde tampon küçültülür; büyük tampon bazı yönlendiricilerde düşer. [windirstat]
- Boyut, diskte gerçekten kaplanan alandır (ayrılmış boyut); sıkıştırılmış ve seyrek dosya
  ayrı sayılır. [npkill, mft]
- **Hızlı kip (yönetici):** önyükleme kesimi, MFT kayıt 0, `$DATA` çalışma listesi, 8 MB
  parçalarla okuma ve fixup; sonraki taramalar `FSCTL_READ_USN_JOURNAL` ile artımlı. [argus, mft, windirstat]
- `FSCTL_ENUM_USN_DATA` dosya boyutu taşımaz; bu yol yalnız ağaç için, boyut MFT'den okunur. [UsnParser]
- Silinmiş ama yeniden kullanılmamış MFT kaydı "kullanımda" bayrağıyla elenir. [UsnParser]
- Yol çözümü `NtCreateFile(FILE_OPEN_BY_FILE_ID)` ve LRU önbelleğiyle tembel yapılır. [UsnParser]
- Her yol `\\?\` önekiyle; MAX_PATH'te kırılmaz.
- **Reparse point'e girilmez.** Junction, symlink ve birim bağlama noktası
  `FSCTL_GET_REPARSE_POINT` etiketiyle ayrılır ve bir kez sayılır. [windirstat]
- **Hard link aynı kimlikle bir kez sayılır** (`FILE_ID_INFO`). İncelenen tarayıcıların hiçbiri
  Windows'ta bunu tam yapmıyor; kendimiz yazıyoruz. [gdu, argus, UsnParser]
- **Bulut yer tutucusu** (`RECALL_ON_DATA_ACCESS`, `OFFLINE`) diskte yer tutmaz; boyutu
  "bulutta" diye ayrı gösterilir, silme teklifine girmez.
- Karantina kökleri taramadan dışlanır.
- Sonuç akışla gelir; arayüz 4 milyon dosyada da donmaz. İptal her an; atomik bayrak ve
  `Continue / Skip / Quit` dönüşüyle dal atlanabilir. Silmede iptal, süren öğeyi bitirir,
  kalanı korur. [dua-cli, ignore]
- Gömülü üçüncü taraf ikili çalıştırılmaz. [squirreldisk]
- Ölçüm hedefi aşama 1'de gerçek diskte konur; ölçülmemiş sayı README'ye girmez.

## 2. Kullanım Sinyalleri

`LastAccess` güvenilmez (NTFS'te varsayılan kapalı). Sinyaller güven sırasıyla:

| Sinyal | Kapsadığı | Not |
|---|---|---|
| Steam `appmanifest_*.acf` `LastPlayed`, `SizeOnDisk` | Steam oyunları | Tüm kütüphaneler `libraryfolders.vdf`'den |
| Epic `Manifests/*.item`, GOG kayıt defteri, Battle.net, EA, Ubisoft | Diğer oyunlar | Launcher başına bir okuyucu, tek arayüz altında. Yalnız "kurulu mu, nerede" güvenilir; "ne zaman oynandı" Prefetch ve UserAssist'ten exe eşlemesiyle [Playnite, legendary] |
| `C:\Windows\Prefetch\*.pf` | Exe'nin son 8 çalışması | Yönetici ister. MAM açma `ntdll` Xpress Huffman ile; Win10'da çalışma sayısı kayması bilinir. EricZimmerman/Prefetch (MIT) referans [PECmd] |
| UserAssist (ROT13) | GUI programların çalışma sayısı, son tarihi, odak süresi | Kullanıcı başına; eski ve yeni biçim ofsetleri farklı [RegistryPlugins] |
| Oynatıcı son dosyaları (VLC `vlc-qt-interface.ini`, MPC-HC kayıt MRU) | Film, dizi | Hazır kitaplık yok, biçimler elle okunur [inceleme 05] |
| Jump list / Recent | Belge klasörleri | |
| `LastWrite` | Son çare | "Değişmedi" demek, "kullanılmadı" değil |

Sinyal yoksa **"bilinmiyor"** yazılır, ayrı bantta durur; sıfır boşta süresi sayılmaz.
Steam VDF ayrıştırması SteamKit2 `KeyValue` ile, NuGet paketi olarak (LGPL-2.1, kaynak
kopyalanmaz). [SteamKit, Playnite]

Amcache ve SRUM atıldı: kilitli hive ve ESE, VSS ister, Prefetch + UserAssist kapsıyor. [srum-dump]

## 3. Birimler — Toplu Teklif

Dosya tek tek değil, amacıyla teklif edilir. Her birim: ad, tür simgesi, boyut, son kullanım,
**neden** satırı, güven, nasıl kaldırılacağı.

| Birim | Nasıl bulunur | Nasıl kaldırılır |
|---|---|---|
| Oyun | Launcher manifestleri | Launcher üzerinden (`steam://uninstall/<appid>`, Epic); klasör elle taşınmaz |
| Program | Uninstall anahtarları (HKLM, HKCU, WOW6432Node) + MSIX paketleri, `InstallLocation` boyutu | Bölüm 6 akışı |
| Film | Tek büyük video + altyazı/afiş aynı klasörde | Karantina |
| Dizi | `S01E02` / `1x02` deseni, sezon klasörleri | Karantina, sezon ya da bütün dizi |
| Geliştirici artığı | İşaret dosyası ve artık klasör tablosu, 20 ekosistem: `package.json` → `node_modules`, `Cargo.toml` → `target`, `*.csproj` → `bin/obj`, Gradle, CMake, Unreal, Godot, Zig, Dart/Flutter, Elixir, Swift, Terraform. Tablo `rules/dev-artifacts.json` içinde | Doğrudan sil, yeniden üretilir [kondo, npkill] |
| Önbellek ve geçici | Temp, küçük resim önbelleği, çökme dökümleri, Delivery Optimization | Doğrudan sil |
| İndirilen kurulum | `Downloads` içinde `.exe/.msi/.iso/.zip`, yanında açılmış kopyası olan arşiv | Karantina |
| Sistem artığı | `SoftwareDistribution\Download`, `Windows.old`, bileşen deposu | Servis durdur / `cleanmgr` / `DISM /StartComponentCleanup`; `/ResetBase` asla; elle değil [windows-docs-pr] |
| Tarayıcı önbelleği | Chrome, Edge, Firefox profilleri: yalnız HTTP, Code, GPU, ServiceWorker önbelleği; geçmiş, çerez, parola asla | Tarayıcı kapalıyken doğrudan sil [CacheFlow, bleachbit] |
| Klasör | Yukarıdakilere uymayan büyük ve eski klasör | Karantina |

Ertelenenler: yinelenen dosya (boyut, 4 KB ön ek hash, son ek hash, tam hash; yalnız >10 MB;
işlem anında dosyanın hâlâ eşleştiği yeniden doğrulanır) [fclones, czkawka], görsel benzerlik
(blok ortalama renk) [dupeguru], fotoğraf albümü, Docker/WSL vhdx (yalnız gösterilir,
sıkıştırma komutuna yönlendirilir). Tarayıcı önbelleği erteleme listesinden çıktı.

## 4. Puanlama

`puan = log(boyut) × boşta_süre_ağırlığı × güven`. Ham boyut çarpımı yok; 200 GB'lık bir
imaj her şeyi ezmesin. Liste varsayılan puana göre, başlığa tıklayınca boyut ya da tarihe
göre. Her teklif "neden" satırı taşır: "Son oynanma 14 ay önce, 87 GB".

## 5. Güvenli Silme ve Karantina

- **Karantina birim başına:** `X:\.dustybytes\quarantine\`, gizli + sistem, ACL kısıtlı.
  İçerik GUID adla, orijinal yol, ACL, öznitelik ve zaman damgaları SQLite manifestinde.
- Aynı birimde yeniden adlandırma: anlık, yer istemez. **Birimler arası kopyala-sil asla.**
- Yazılabilir kök yoksa (salt okunur, ağ, exFAT) `IFileOperation` + `FOF_ALLOWUNDO` ile
  Geri Dönüşüm Kutusu; o da yoksa gerekçesiyle reddedilir.
- Arayüz iki sayı gösterir: **Bekleyen** (karantinada) ve **Açılan** (kesin silinen).
  Karantina yer açmaz; kullanıcı bunu görür.
- Geri Dönüşüm Kutusu yolunda geri yükleme bilgisi kutunun kendi `SCID_ORIGINAL_LOCATION`
  ve `SCID_DATE_DELETED` özelliklerinden okunur. [trash-rs, Vanara]
- **Kullanıcı verisi** (kayıtlı oyun, belge, ayar) varsayılan korunur, ayrı açık onay ister. [Scoop persist]
- **Açık dosya:** Restart Manager (`RmStartSession`, `RmRegisterResources`, iki geçişli
  `RmGetList`) tutan süreci adıyla gösterir, kapatmayı teklif eder;
  kabul yoksa yeniden başlatmaya ertelenir. Süreç sessizce öldürülmez.
- Süre dolan karantina uygulama açılışında boşaltılır, hizmet yok. Birimin %20'sini geçerse uyarı.
- Geri yükleme: orijinal yol doluysa çakışma ekranı, üst klasör yoksa yeniden kurulur.
- Raf kuralı: geri alınabilir eylem onaylatılmaz, yapılır ve "Geri al" sunulur. Karantinadan
  kalıcı silme ayrı, açık bir eylemdir.

**Korumalı liste (worker içinde, kodda değil `rules/protected.json` içinde)** [bleachbit ProtectedPath].
Kullanıcı elle istisna ekleyebilir [choco .skipAutoUninstall]. Kalıcı Appx listesi: Terminal,
DesktopAppInstaller, StorePurchaseApp, WSL, codec uzantıları [Sophia-Script]. Yollar: `Windows`, `WinSxS`, `System Volume Information`,
`hiberfil/pagefile/swapfile` ("sistem, ayarlardan" yazılır), OneDrive / Dropbox / Google Drive
kökleri ("bulut, senkron" rozeti), `Common Files`, `Microsoft Shared`,
`ProgramData\Package Cache`, VC++ / .NET / Java çalışma zamanları, launcher kütüphane kökleri.

## 6. Program Kaldırma ve Kalıntı

1. Geri yükleme noktası (`SRSetRestorePoint`).
2. **Önce anlık görüntü:** yüksek güvenli kalıntı adayları kaydedilir.
3. Üreticinin kaldırıcısı: `QuietUninstallString`, MSI ise `msiexec /x {ProductCode}`.
   MSIX/Store paketi `PackageManager` ile; kalıntı araması gerekmez.
4. **Fark:** kaldırıcının silmedikleri gerçek kalıntıdır.
5. Kalıntı kimlikle eşlenir, adla değil: exe sürüm kaynağı (`CompanyName`, `ProductName`),
   Uninstall anahtarı (`InstallLocation`, `Publisher`), Authenticode imzalayan. MSI'da
   `MsiEnumComponents` kesin listeyi verir.
6. **Ağırlıklı puan ve iki çapa:** her kanıt puan taşır (Uninstall anahtarı artı, klasör başka
   programın `InstallLocation` altında eksi, Store paketi eksi) [BCUninstaller ConfidenceRecords].
   Toplam puan tek başına yetmez; **iki çapa kuralı:** aday en az iki bağımsız kanıt taşır; tek çapa düşük güvendir, gizli gelir.
   "Tools", "Update", "Client" gibi genel ad parçaları eşleşme sayılmaz.
7. Klasör başka kurulu programın `InstallLocation` altındaysa aday düşer [BCUninstaller
   CheckIfDirIsStillUsed]. **`Downloads`, `Desktop`, `Documents` ve taşınabilir uygulama klasörü
   kalıntı olamaz**; BCUninstaller'ın yanlış silme issue'larının hepsi bu sınıftan [BCU #504 #611 #751 #753].
   Appx eşlemesi yalnız `PackageFullName` ile, joker karakter yok [winutil, negatif örnek].
   Paylaşılan yayıncı (Microsoft, Adobe, NVIDIA, Google) anahtarında yalnız ürün alt anahtarına dokunulur.
8. Kurulu program listesi: HKCU/HKLM × 32/64-bit dört Uninstall görünümü ve MSIX; kurucu türü
   (MSI, Inno, NSIS, InstallShield) imza deseniyle tahmin edilir, MSI sıkıştırılmış GUID çözülür
   [winget-cli, choco]. Taranan yerler: kurulum klasörü, AppData (Roaming, Local, LocalLow), ProgramData,
   `HKCU/HKLM\Software`, servisler, zamanlanmış görevler, başlangıç girdileri, kısayollar,
   dosya ilişkileri, güvenlik duvarı kuralları.
9. Onay arayüzde değil worker'da zorunlu; arayüz atlatılsa da onaysız kaldırma olmaz
   [Win11Debloat #650]. Güven katmanı: **Yüksek** işaretli gelir, **Orta** işaretsiz, **Düşük** "daha fazla göster"
   arkasında. Orta ve Düşük asla kendiliğinden gitmez.
10. Kayıt defteri kalıntısı silinmeden `.reg` olarak karantinaya dışa aktarılır.

BCUninstaller (Apache-2.0) tek gerçek rakip; winget, Scoop ve Chocolatey dosya sistemi
kalıntısı taramıyor (winget #1117 açık). Fark burada. [inceleme 03]

## 7. Temizlik Kuralları (CCleaner Benzeri)

- Kural biçimi CleanerML'in dört alanı: eylem, uyarı, çalışıyor testi, kimlik. BleachBit
  tanımları (GPL-3.0) başlangıç kaynağı. [bleachbit]
- Çalışıyor testi iki kanıtlı: exe adı ve kilit dosyası (Firefox `parent.lock` gibi). [bleachbit]
- winapp2.ini CC-BY-SA-4.0, veri lisansı: koda gömülmez, ayrı veri dosyası olarak ve atıfla
  gelir; 4.068 kural. [Winapp2]
- Her ödünç `docs/licenses.md`'ye yazılır. optimizer arşivlendi, referans alınmaz.

## 8. Arayüz

Raf kuralları bağlar: yalnız koyu tema, renk ve ölçü `--tk-*` token'larından, kendi başlık
çubuğu (Snap, sürükleme, `Alt+F4` geri verilir), standart üst çubuk ve imza, tema kitaplığı
yok, hareket temel, yalnız `transform` ve `opacity`.

| Ekran | İçerik |
|---|---|
| Genel Bakış | Birim başına disk çubuğu; kahraman sayı "Açılabilir alan"; ilk beş teklif; son tarama saati |
| Öneriler | Birim kartları puana göre; tür süzgeci (Oyun, Film, Program, Geliştirici, Önbellek); çoklu seçim; alt şeritte seçilenin toplamı ve tek birincil eylem |
| Harita | Squarified treemap, d3-hierarchy `squarify` (ISC) mantığından sıfırdan C#; `ICustomDrawOperation` ve `ISkiaSharpApiLeaseFeature` ile tek çizim, hit-test elle; eşik altı küçük öğeler "Diğerleri" diliminde; seçilenin üst zinciri vurgulu, gerisi sönük; tıkla içine gir, üstte yol kırıntısı [Avalonia, d3-hierarchy, filelight, qdirstat] |
| Programlar | Kurulu programlar: boyut, son kullanım, yayıncı; "Kaldır" kalıntı önizlemesiyle açılır |
| Temizlik | Önbellek ve sistem artığı kuralları, program başına aç/kapa |
| Karantina | Bekleyen öğeler, kalan gün, Geri Al, Kalıcı Sil; Bekleyen ve Açılan sayıları |

- Tarama sırasında: adım adı tam cümleyle ("Steam kütüphanesi okunuyor"), yüzde çubuğun
  yanında, son dokuz günlük satırı; çubuk tavan kuralıyla sürünür, geri gitmez.
- Başlık çubuğu `ExtendClientAreaToDecorationsHint` ve `SetNonClientHitTestResult`; Snap,
  sürükleme, `Alt+F4` yerel akışla korunur. [Avalonia WindowImpl]
- Geçişler compositor `ImplicitAnimationCollection` ile, yalnız opacity ve scale. [WalletWasabi]
- Gezinme tek `ViewModelBase` ve yığın; her ekran ayrılırken kaynak bırakır. [WalletWasabi]
- Liste değişiminde kalan kartlar yeni yerine kayar (FLIP); silinen kart akıştan düşer.
- Önbellekten açılış: iskelet değil, eski sonuç hemen görünür, üstte "Tazeleniyor" rozeti.
- Boş ve hata ekranı sonraki adımı söyler, tek birincil eylem taşır.
- Başlangıç: Native AOT hedeflenir, olmazsa ReadyToRun ve trimming; yansıma tabanlı bağlama
  yok, derlenmiş bağlama [Avalonia BuildTests.NativeAot]. Yer açan program şişman açılmaz.
- Treemap renkleri birim türüne göre; pembe ile mor yan yana tek ayırıcı olmaz.

## 9. Test ve Doğrulama

- Birim testleri: sahte disk ağaçları, örnek `.pf`, `.acf`, Uninstall kayıtları `tests/fixtures`'ta.
- `KabukStandardiTests`: raftaki standart `docs/tasarim/`'e kopyalanır, ölçülebilen her kural testlenir.
- **Korumalı liste testleri önce yazılır:** junction içinden silme, bulut yer tutucusu,
  paylaşılan bileşen, launcher klasörü. Dördü de kırmızıdan yeşile geçmeden silme kodu birleşmez.
- Kaldırma testleri Windows Sandbox'ta: gerçek NSIS, Inno, MSI kurucu kur, kaldır, farkı ölç.
- **Prova kipi ilk sürümden zorunlu** (BCUninstaller'da en çok istenen eksik, #947): ortam değişkeniyle her silme ve kaldırma günlüğe yazılır ama yapılmaz.
- `uc` denetimi: gerçek exe penceresi %100, %125, %150; iki iddia iki kanıt.

## 10. Aşamalar

- [ ] **A0 İskelet:** git, `.gitignore` (`tmp/`), çözüm ve projeler, AGPL, `AGENTS.md`,
      teknesyum-ui bağlanması, `Theme.axaml`, kabuk standardı testleri, üst çubuk,
      CsWin32 FSCTL deneme derlemesi, `docs/licenses.md`.
- [ ] **A1 Salt Okunur Tarama:** FindFirstFileEx tarayıcı, SQLite dizin, Genel Bakış, Harita,
      "büyük ve LastWrite'ı eski" listesi. Silme yok, yönetici yok. Gerçek diskte ölçüm.
- [ ] **A2 Güvenli Silme:** worker + pipe, korumalı liste ve testleri, prova kipi, karantina,
      Geri Al, Restart Manager, ilk birimler: geliştirici artığı (kondo tablosu), önbellek,
      tarayıcı önbelleği, indirilen kurulum.
- [ ] **A3 Sinyal ve Puan:** Steam, Epic, GOG, Prefetch, UserAssist, oynatıcılar; oyun, film,
      dizi birimleri; puanlama; Öneriler ekranı.
- [ ] **A4 Kaldırıcı:** Programlar ekranı, anlık görüntü + fark, kimlikle eşleme, güven katmanı.
- [ ] **A5 Temizlik:** CleanerML kuralları, sistem artığı (DISM, `cleanmgr`, servis).
- [ ] **A6 Hızlı Tarama:** MFT + USN artımlı, "Hızlı tarama (yönetici)".
- [ ] **A7 Yayın:** Kur penceresi, güncelleme rozeti, kod imzası, README EN + TR, `uc` denetimi.

## Riskler

| Risk | Kapı |
|---|---|
| Geri getirilemeyen silme | Karantina, korumalı liste worker'da, A2'de testler önce |
| Bulut yer tutucusu silinip buluttan da gitmesi | Tarayıcıda öznitelikle ayırma, bulut kökleri korumalı |
| Junction içinden hedef silme | Reparse point'e girilmez |
| Kalıntı taramasının paylaşılan bileşeni götürmesi | İki çapa, kimlikle eşleme, beyaz liste, `.reg` yedek |
| Launcher'dan habersiz taşınan oyun | Oyun yalnız launcher üzerinden kaldırılır |
| Büyük diskte donan arayüz | Akışlı sonuç, ayrı süreç, iptal |
| Defender/SmartScreen engeli | Kod imzası sertifikası A7 bütçesinde |

## İnceleme Dizini

| Rapor | Depolar |
|---|---|
| [01 Disk Çözümleyiciler](inceleme/01-disk-cozumleyiciler.md) | windirstat, gdu, dust, dua-cli, qdirstat, filelight, squirreldisk |
| [02 MFT ve İndeks](inceleme/02-mft-indeks.md) | omerbenamram/mft, CsWin32, UsnParser, NtfsReader, argus, ntfs-cpu-search, fd, ignore, walkdir, jwalk, libfsntfs, Windows-classic-samples |
| [03 Kaldırıcılar](inceleme/03-kaldiricilar.md) | Bulk-Crap-Uninstaller, winget-cli, Scoop, choco, winutil, Win11Debloat, Sophia-Script |
| [04 Temizleyiciler](inceleme/04-temizleyiciler.md) | bleachbit, Winapp2, optimizer, czkawka, Mole, windows-docs-pr, CacheFlow |
| [05 Kullanım Sinyalleri](inceleme/05-kullanim-sinyalleri.md) | Prefetch, PECmd, RegistryPlugins, Playnite, PlayniteExtensions, SteamKit, Heroic, legendary, libscca, srum-dump, trakt-scrobbler |
| [06 Artık, Yinelenen, Çöp](inceleme/06-artik-yinelenen-cop.md) | npkill, kondo, fclones, dupeguru, rmlint, trash-rs, trash-cli, Vanara, Verify |
| [07 Avalonia Arayüz](inceleme/07-avalonia-arayuz.md) | Avalonia, WalletWasabi, SukiUI, Ursa, Beutl, d3-hierarchy, menees/Treemap, Polymerium |

Kod alınmayacaklar: qdirstat (GPL-2.0-only), menees/Treemap (MSR-SSLA), ntfs-cpu-search
(lisanssız), optimizer (arşiv), jwalk (bırakıldı), squirreldisk (bakımsız, gömülü ikili).

## Açık Konular

- teknesyum-ui A0'da `setup.js` ile bağlanır; `teknesyum-ui.json` ve token'lar gelmeden renk yazılmaz.
- Kod imzası sertifikası: satın alma sahibin kararı, A7'den önce.

## UI Denetimi (uc) — 2026-09-27

Kaynak: raf `ui-denetim`, `ui-duzeni`; eklenti teknesyum-ui 0.11.0. Rapor `docs/ui-denetim/2026-09-27.md`.

1. Kurulum: `setup.js --apply` projeyi `neon`dan sahibin kayıtlı düzenine (`benim.tokens.json`) geçirir; Theme.axaml yeniden üretilir, uygulama stilleri yeni kaynak adlarına bağlanır.
2. Üst çubuk, güncelleme rozeti/paneli ve kurulum ekranı eklentinin Avalonia şablonlarından (`scaffold.js ustcubuk|durum|kur --avalonia`) gelir; elle yazılmış eşleri `trash/`a.
3. Ekran envanteri: altı sekme, kaldırma akışı, iletişim kutuları, güncelleme paneli; her birinin boş, hata ve yükleme hâli.
4. Durağan tarama `scan.js --fix`, kalan bulgu 0.
5. Canlı kontrast: `scaffold.js denetim DustyBytes` başsız testi; her yazı ve simge her durumda gerçek zeminine karşı 7:1.
6. Gerçek pencere görüntüsü %100, %125, %150; önce ve sonra.
7. İşi yapmamış alt ajan görüntülere bakar; bulgular kapanana dek 4–7 yinelenir.
8. Raf kitapları: depo, guncelleme-paneli, kabuk-standardi, kurulum-paneli, lisans, README-kabuk-standardi, readme-protokolu, ui, ui-duzeni; her biri `raf.js --uydu` ile kaydedilir.

Durum 2026-09-27: 1–5, 7 ve 8 tamam. 6 gerçek pencerede yalnız %100 (ölçek sistem ayarı), %125/%150 başsız. Kur penceresinin kaynağı şablon işine bağlı (Teknesyum-UI). Rapor: `docs/ui-denetim/2026-09-27.md`.

## A9 Fark: Büyük İçerik Ve Tek Tık — 2026-09-27

Sahibin isteği: kullanıcıyı yormadan en net veriyi getir. 600 MB önbellek değil, "LM Studio · 40 GB dil modeli".
Tek tık karantina, onay yok; 7 gün sonra kendiliğinden silinir; karantina anında, onaysız boşaltılır.
Araştırma: `docs/danisma/buyuk-icerik-arastirmasi.md`.

1. `Unit.Effect`: her birime sade dille "silinirse ne olur". Tanıyıcı kendi cümlesini yazar; yoksa türün cümlesi (`KindText.Effect`).
2. `UnitKind.AppContent` + `rules/known-content.json` + `KnownContentExtractor`: bilinen ağır içerik yerleri
   (dil modelleri, görüntü modelleri, emülatör ve sanal diskler, paket önbellekleri, telefon yedekleri, kayıtlar).
   Birim adı içeriğin kendi adı, etiketi sahibinin adıyla türü ("LM Studio · Dil modeli"). Öğe başına en az 1 GB.
3. Öneriler 1 GB altını gizler; alt satırda "Küçükleri de göster (n birim, x GB)".
4. Kartta tek düğme: "Karantinaya al" (önbellek için "Temizle"). Onay kutusu yok; bildirimde "Geri al".
   Kullanıcının tıkladığı karantina `IncludeUserData` taşır: İndirilenler ve Videolar'daki birimler artık reddedilmez.
5. Karantina süresi 7 gün. Worker açılışta ve açık kaldıkça saatte bir süresi dolanı siler.
6. Karantina ekranı: "Karantinayı boşalt" onaysız (`purge` + `Target=all`); seçili kalıcı silme de onaysız.
7. Testler: çıkarıcı, boşaltma, onaysız akış; gerçek pencere görüntüsü; README.

Güvenlik sınırı değişmez: yetkisiz arayüz dosyaya dokunmaz, her istek worker'da korumalı listeden geçer,
yönetici yetkisiyle zamanlanmış görev kurulmaz (kullanıcı yazabilen exe'yi yükseltmek açık olurdu).

## A10 Oyunlarda Tek Tık Ve Hızlı Açılış — 2026-09-27

Ölçüm: `docs/olcum/2026-09-27-yukleme.md`.

1. `GameInstall.Manifest`: Steam `appmanifest_<id>.acf`, Epic `.item` yolu okunur.
2. Manifesti bilinen oyun (Steam, Epic) karantina birimi olur: yollar kurulum klasörü ve manifest. Kayıt klasörleri
   birime girmez, yerinde kalır. Geri alınınca ikisi birlikte döner, başlatıcı oyunu yine kurulu görür.
   Manifesti bilinmeyen oyun (GOG, Ubisoft, Xbox) eskisi gibi "Başlatıcıda aç".
3. `ProtectedList.CheckGamePath`: başlatıcı kütüphane kökünün yalnız içindeki klasöre izin verir; kökün kendisi,
   üst klasörü ve bütün sistem kökleri eskisi gibi reddedilir. Worker'ın kuralı değişmez.
4. Oyun kartının etkisi: karantina, kayıtlar yerinde, 7 gün, geri alınabilir.
5. `UnitBuilder` çakışma denetimi karesel taramadan ata kümesine iner.
6. Testler: manifest okuma, oyun birimi, kütüphane kökü reddi, kart düğmesi; ölçüm önce ve sonra.

## A12 Hızlı Tarama Akışı — 2026-09-28

Kaynak: [danışma 002](danisma/002-fable-dustybytes-tarama-akisi.md). Sahibin sözü: "tarama kısmı halen yavaş işliyor
tarama yaptıkça silinebilir özellikleri bastıralım görelim".

1. Kaydetme bekleme yolundan çıktı (ce331a2): normal taramada −7/8 sn.
2. Hızlı taramada worker ağacı SQLite yerine ikili dosyayla (`ScanTreeCodec`) geçirir; SQLite kaydını arayüz
   arka planda yapar (tek yazıcı, iki süreç aynı dizine yazmaz). Hedef: hızlı tarama başlat → kartlar ≤ 11 sn.
3. USN artımlı yenileme varsayılan: önbellek yükle → imleç geçerliyse `UsnUpdater` → birimler. Tam tarama yalnız
   indeks yoksa, günlük sıfırlandıysa ya da "Baştan tara" denirse. Yetkisiz okuma (`FSCTL_READ_UNPRIVILEGED_USN_JOURNAL`)
   önce denenir, olmazsa worker (`Ops.UsnRefresh`). Ölçüt: yenileme sonucu = tam tarama sonucu (birim kimlikleri ve toplam boyut).
4. Worker zaten ayaktaysa normal tarama MFT'yle yapılır.
5. Bulundukça gösterme: ağaçtan bağımsız türler (oyun, program, önbellek, kurulum dosyası, sistem artığı) tarama
   sürerken kesin boyutla görünür; film, dizi, büyük klasör, geliştirici artığı tarama bitince. Kartlar akarken
   yeniden sıralanmaz; boyutu kesinleşmemiş kart seçilemez.

Ölçümler `docs/olcum/2026-09-28-tarama.md`.

## A13 Kalıntısız Kaldırma — 2026-09-28

Sahibin sözü: "artık regedit keyi falan bırakmadan temiz çalışarak sil". Araştırma: `danisma/kaldirma-arastirmasi.md`,
fable görüşü 003 (`danisma/003-fable-dustybytes-kalintisiz-kaldirma.md`).

Hedef: Kaldır → tek onay → bitti (2 tık). Kalıntı üç kovada: Yüksek + yol kanıtı onaysız temizlenir
(`.reg` yedeği ve 7 günlük karantinayla), Orta gösterilir işaretsiz, engel listesi hiç dokunulmaz.
Seçenek "Ayarları koru" işaretsiz; varsayılan kalıntısız.

**Dalga 1**
1. Tek adımda temizlik: `UninstallHandlers` sonunda `LeftoverRemover.Remove` yalnız `AutoRemovable`
   (Yüksek + yol kanıtı); `Messages.cs` `AutoClean` bayrağı; `UninstallViewModel` tek onay.
2. Araca göre sessiz kaldırma: `QuietUninstallString` → MSI `/qn`, Inno `/VERYSILENT`, NSIS `/S`,
   Burn `/uninstall /quiet`, Squirrel `-s`; başarısız ya da hâlâ kuruluysa görünür kaldırıcıya düş,
   kullanıcıya "sihirbazı siz bitirin, biz bekliyoruz" kartı. Sihirbaz otomatik tıklanmaz.
3. Kaldırma sonrası ikinci anlık görüntü: `LeftoverScanner.Diff`.

**Dalga 2**
4. Değer düzeyi kayıt temizliği (Run + `StartupApproved`, `App Paths`, `OpenWithProgids`).
5. COM ve kabuk uzantıları (`LeftoverScanner.Com.cs`), yol kanıtıyla.
6. Kurulum klasörü bilinmiyorsa başka kaynak.
7. Doğru kullanıcı: worker boru istemcisinin SID'i, `HKU\<SID>`.

**Sonra**: zorla kaldır, servis durdurma, MSI yetimleri, güvenlik duvarı kimlikle, SharedDLLs,
geri yükleme doğrulaması, MSIX tüm kullanıcılar. Ölçü: sanal makinede A/B/C anlık görüntüsü,
(B−A) ∩ C = 0; negatif test Java kalkar, VirtualBox dokunulmaz (`tools/uninstall-bench/`).

**Durum (2026-09-28)**: Dalga 1 ve 2 bitti (7be7949, 312b12e, 8aa4af1, be5e17e, acb1508, 663934d,
209d72c). Ölçüm araçları yazıldı, çalıştırılmadı (`docs/olcum/kaldirma-gurultu.md`); gerçek makine
testi `DUSTYBYTES_REAL_UNINSTALL` ile açılır. Başlangıç klasörü kısayolunun `StartupApproved` değeri
kodda var, testi gerçek `.lnk` örneği bekliyor.

## A14 Kolay Akış Ve Otomatik Mod — 2026-09-28

Sahibin sözü: "hiç teknik bilgisi olmayan bir kullanıcı bile ai ın en önerdiği seçenekleri turlayacak".

1. **Kalıcı sil iki basışla**: onay penceresi kalkar. İlk basış düğmeyi kırmızıya (`DangerButton`)
   çevirir, metin "Silmek için tekrar basın"; ikinci basış siler. 4 sn içinde basılmazsa ya da
   başka yere tıklanırsa eski haline döner. Kart ve alt çubuk aynı davranış.
2. **Öneriler tek satır**: her birim bir satır — onay kutusu, ad, tür çipi, son kullanım, boyut,
   eylemler. Yol ve açıklama ipucunda. Satırın her yerine basmak seçimi değiştirir; onay kutusunun
   basılabilir alanı büyür.
3. **Tümünü seç**: listenin üstünde üç durumlu kutu; yalnız görünen ve toplu silinebilen birimler.
4. **Otomatik mod**: Genel bakış ve Öneriler'de "Otomatik temizle" birincil düğmesi.
   a. Emin olunanlar sorulmadan: Temizlik ekranının uyarısız, varsayılan işaretli seçenekleri
      (geçici dosyalar, önbellekler) ve `DirectDelete` politikalı birimler temizlenir.
   b. Tur: büyük ve uzun süre kullanılmamış birimler öneri sırasıyla birer birer gösterilir —
      "Bunu silmek ister misiniz?"; Karantinaya al (birincil), Kalıcı sil (iki basış), Kalsın, Turu bitir.
      Üstte "3 / 12" ve açılan toplam yer. Kullanıcı verisi olan birim uyarıyla gösterilir, asla sorulmadan gitmez.
   c. Bitiş özeti: temizlenen, karantinaya alınan, kalıcı silinen, açılan yer; Geri al karantinayı açar.
5. Geçişler akıcı: tur kartı değişiminde teknesyum-ui hareket token'ları; hiçbir adım pencere açmaz.

Güvenlik değişmez: her silme worker'da SafetyGate'ten geçer; otomatik adım yalnız zaten
sorusuz temizlenen kategorilere dokunur.

## A15 Piyasanın En Pratiği — 2026-09-30

Sahibin sözü: "piyasadaki en iyi olacaz en pratik olacaz ... ne ararsan bizde kullanıcı dostu olacaz".
Kaynak: `docs/danisma/2026-09-30-piyasa-karsilastirmasi.md` (20 eksik, sıralı).

**Dalga 1**, dört ayrı çalışma ağacında paralel:

1. **Bütün sürücüler** (eksik 1): sabit ve çıkarılabilir her NTFS/exFAT birimi taranır; sürücü başına
   dizin ve USN imleci; Genel bakışta sürücü seçici, "Tümü" varsayılan. Karantina birim başına kalır.
2. **Yeni öneri kaynakları** (eksik 4, 5, 10): Geri Dönüşüm Kutusu (30 günden eskiler işaretli),
   İndirilenler'de 90 günden uzun açılmamış dosyalar (karantinayla), hazırda bekletme kartı
   (yalnız masaüstünde önerilir, dizüstünde uyarıyla; geri alınabilir).
3. **Silmeden yer aç** (eksik 6, 7): oyun ve program kartında "Küçült" (WOF saydam sıkıştırma, geri
   alınabilir, yalnız NTFS); OneDrive'da indirilmiş ve uzun süre açılmamış dosyalar "yalnız çevrimiçi".
4. **Kendiliğinden hatırlatma** (eksik 2, 9, 11, 12): kullanıcı düzeyinde haftalık görev
   `--check` boş alanı ölçer, azsa Windows bildirimi gösterir, silmez; disk %10'un altındaysa
   "Bekleyenleri şimdi kalıcı sil" iki basışlı düğme; açılan yer geçmişi ve önce/sonra çubuğu;
   Explorer sağ tık "DustyBytes ile incele" (HKCU, yönetici gerekmez).

**Dalga 2**: yinelenen dosya (eksik 8, yalnız karantina, işlem anında yeniden doğrulama), zorla
kaldırma (14), toplu kaldırma (15).

**Sonra**: başlangıç programları (13, disk dışı), başka sürücüye taşı (16), gölge kopya alanı (18),
tarayıcı kapanınca temizle (19, sürekli süreç ister), paket yazılım tespiti (20).

Güvenlik değişmez: her silme, taşıma, sıkıştırma ve bulut çözme isteği worker'da SafetyGate'ten
geçer; arayüz dosyaya dokunmaz; kullanıcı verisi hiçbir zaman sorulmadan gitmez.
