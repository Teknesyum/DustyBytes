# DustyBytes Planı

Diski tarar, büyük ve az kullanılanı üste koyar, amaca göre toplar ("şu oyun, şu film, şu
klasör") ve tek tıkla yer açar. Program kaldırınca kalıntı bırakmaz. Format atmaya gerek kalmaz.

Kaynaklar: özel raf (`kimlik`, `calisma`, `depo`, `araclar`, `lisans`, `readme-protokolu`,
`ui`, `ui-duzeni`, `ui-denetim`, `kabuk-standardi`, `README-kabuk-standardi`,
`guncelleme-paneli`, `bilesen-surumleri`, `yazim`) ve fable görüşü
[001](danisma/001-fable-dustybytes-plan.md).

## Kararlar

| Konu | Karar | Gerekçe |
|---|---|---|
| Yığın | Avalonia 11 + .NET 10 LTS, C# | Raf: ağır yerel iş Avalonia. Kabuk standardı ve testleri Avalonia için yazılı. |
| Windows API | CsWin32 kaynak üreteci | MFT, USN, Restart Manager, MSI, kayıt defteri tip güvenli P/Invoke. |
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
  Yönetici istemez.
- **Hızlı kip (yönetici):** NTFS MFT doğrudan okuma, sonraki taramalar USN günlüğüyle artımlı.
- Her yol `\\?\` önekiyle; MAX_PATH'te kırılmaz.
- **Reparse point'e girilmez.** Junction ve symlink bir kez, dosya kimliğiyle sayılır. Hard
  link aynı kimlikle bir kez sayılır.
- **Bulut yer tutucusu** (`RECALL_ON_DATA_ACCESS`, `OFFLINE`) diskte yer tutmaz; boyutu
  "bulutta" diye ayrı gösterilir, silme teklifine girmez.
- Karantina kökleri taramadan dışlanır.
- Sonuç akışla gelir; arayüz 4 milyon dosyada da donmaz. İptal her an.
- Ölçüm hedefi aşama 1'de gerçek diskte konur; ölçülmemiş sayı README'ye girmez.

## 2. Kullanım Sinyalleri

`LastAccess` güvenilmez (NTFS'te varsayılan kapalı). Sinyaller güven sırasıyla:

| Sinyal | Kapsadığı | Not |
|---|---|---|
| Steam `appmanifest_*.acf` `LastPlayed`, `SizeOnDisk` | Steam oyunları | Tüm kütüphaneler `libraryfolders.vdf`'den |
| Epic `Manifests/*.item`, GOG kayıt defteri, Battle.net, EA, Ubisoft | Diğer oyunlar | Launcher başına bir okuyucu |
| `C:\Windows\Prefetch\*.pf` | Exe'nin son 8 çalışması | Yönetici ister, Win10+ sıkıştırmalı (MAM) |
| UserAssist (ROT13) | GUI programların çalışma sayısı ve son tarihi | Kullanıcı başına |
| Oynatıcı son dosyaları (VLC, MPC-HC, PotPlayer, Filmler ve TV) | Film, dizi | İzlendi mi |
| Jump list / Recent | Belge klasörleri | |
| `LastWrite` | Son çare | "Değişmedi" demek, "kullanılmadı" değil |

Sinyal yoksa **"bilinmiyor"** yazılır, ayrı bantta durur; sıfır boşta süresi sayılmaz.
Amcache atıldı (kilitli hive, VSS ister, Prefetch + UserAssist kapsıyor).

## 3. Birimler — Toplu Teklif

Dosya tek tek değil, amacıyla teklif edilir. Her birim: ad, tür simgesi, boyut, son kullanım,
**neden** satırı, güven, nasıl kaldırılacağı.

| Birim | Nasıl bulunur | Nasıl kaldırılır |
|---|---|---|
| Oyun | Launcher manifestleri | Launcher üzerinden (`steam://uninstall/<appid>`, Epic); klasör elle taşınmaz |
| Program | Uninstall anahtarları (HKLM, HKCU, WOW6432Node) + MSIX paketleri, `InstallLocation` boyutu | Bölüm 6 akışı |
| Film | Tek büyük video + altyazı/afiş aynı klasörde | Karantina |
| Dizi | `S01E02` / `1x02` deseni, sezon klasörleri | Karantina, sezon ya da bütün dizi |
| Geliştirici artığı | `node_modules`, `.venv`, `target`, `bin/obj`, Gradle/Maven/NuGet önbelleği | Doğrudan sil (yeniden üretilir) |
| Önbellek ve geçici | Temp, küçük resim önbelleği, çökme dökümleri, Delivery Optimization | Doğrudan sil |
| İndirilen kurulum | `Downloads` içinde `.exe/.msi/.iso/.zip`, yanında açılmış kopyası olan arşiv | Karantina |
| Sistem artığı | `SoftwareDistribution\Download`, `Windows.old`, bileşen deposu | Servis durdur / `cleanmgr` / DISM; elle değil |
| Klasör | Yukarıdakilere uymayan büyük ve eski klasör | Karantina |

Ertelenenler: yinelenen dosya (boyut kovası → kısmi hash → tam hash, yalnız >10 MB),
fotoğraf albümü, tarayıcı önbellekleri (açıkken kilitli), Docker/WSL vhdx (yalnız gösterilir,
sıkıştırma komutuna yönlendirilir).

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
- **Açık dosya:** Restart Manager tutan süreci adıyla gösterir, kapatmayı teklif eder;
  kabul yoksa yeniden başlatmaya ertelenir. Süreç sessizce öldürülmez.
- Süre dolan karantina uygulama açılışında boşaltılır, hizmet yok. Birimin %20'sini geçerse uyarı.
- Geri yükleme: orijinal yol doluysa çakışma ekranı, üst klasör yoksa yeniden kurulur.
- Raf kuralı: geri alınabilir eylem onaylatılmaz, yapılır ve "Geri al" sunulur. Karantinadan
  kalıcı silme ayrı, açık bir eylemdir.

**Korumalı liste (worker içinde):** `Windows`, `WinSxS`, `System Volume Information`,
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
6. **İki çapa kuralı:** aday en az iki bağımsız kanıt taşır; tek çapa düşük güvendir, gizli gelir.
   "Tools", "Update", "Client" gibi genel ad parçaları eşleşme sayılmaz.
7. Paylaşılan yayıncı (Microsoft, Adobe, NVIDIA, Google) anahtarında yalnız ürün alt anahtarına dokunulur.
8. Taranan yerler: kurulum klasörü, AppData (Roaming, Local, LocalLow), ProgramData,
   `HKCU/HKLM\Software`, servisler, zamanlanmış görevler, başlangıç girdileri, kısayollar,
   dosya ilişkileri, güvenlik duvarı kuralları.
9. Güven katmanı: **Yüksek** işaretli gelir, **Orta** işaretsiz, **Düşük** "daha fazla göster"
   arkasında. Orta ve Düşük asla kendiliğinden gitmez.
10. Kayıt defteri kalıntısı silinmeden `.reg` olarak karantinaya dışa aktarılır.

İncelenecek: BCUninstaller (Apache-2.0, AGPL ile uyumlu): imza eşleştirme ve kurulum
tarihi korelasyonu (yalnız destekleyici sinyal).

## 7. Temizlik Kuralları (CCleaner Benzeri)

Program başına temizlik tanımı elle yazılmaz. BleachBit'in CleanerML tanımları (GPL-3.0,
AGPL-3.0 ile birleşir) başlangıç kaynağıdır; winapp2.ini lisansı ayrıca denetlenir. Her
ödünç `docs/licenses.md`'ye yazılır. Program açıkken temizlenmez; Restart Manager söyler.

## 8. Arayüz

Raf kuralları bağlar: yalnız koyu tema, renk ve ölçü `--tk-*` token'larından, kendi başlık
çubuğu (Snap, sürükleme, `Alt+F4` geri verilir), standart üst çubuk ve imza, tema kitaplığı
yok, hareket temel, yalnız `transform` ve `opacity`.

| Ekran | İçerik |
|---|---|
| Genel Bakış | Birim başına disk çubuğu; kahraman sayı "Açılabilir alan"; ilk beş teklif; son tarama saati |
| Öneriler | Birim kartları puana göre; tür süzgeci (Oyun, Film, Program, Geliştirici, Önbellek); çoklu seçim; alt şeritte seçilenin toplamı ve tek birincil eylem |
| Harita | Treemap, Skia `DrawingContext` ile tek çizim, hit-test elle; tıkla içine gir, üstte yol kırıntısı |
| Programlar | Kurulu programlar: boyut, son kullanım, yayıncı; "Kaldır" kalıntı önizlemesiyle açılır |
| Temizlik | Önbellek ve sistem artığı kuralları, program başına aç/kapa |
| Karantina | Bekleyen öğeler, kalan gün, Geri Al, Kalıcı Sil; Bekleyen ve Açılan sayıları |

- Tarama sırasında: adım adı tam cümleyle ("Steam kütüphanesi okunuyor"), yüzde çubuğun
  yanında, son dokuz günlük satırı; çubuk tavan kuralıyla sürünür, geri gitmez.
- Liste değişiminde kalan kartlar yeni yerine kayar (FLIP); silinen kart akıştan düşer.
- Önbellekten açılış: iskelet değil, eski sonuç hemen görünür, üstte "Tazeleniyor" rozeti.
- Boş ve hata ekranı sonraki adımı söyler, tek birincil eylem taşır.
- Başlangıç: ReadyToRun + trimming; yer açan program şişman açılmaz.
- Treemap renkleri birim türüne göre; pembe ile mor yan yana tek ayırıcı olmaz.

## 9. Test ve Doğrulama

- Birim testleri: sahte disk ağaçları, örnek `.pf`, `.acf`, Uninstall kayıtları `tests/fixtures`'ta.
- `KabukStandardiTests`: raftaki standart `docs/tasarim/`'e kopyalanır, ölçülebilen her kural testlenir.
- **Korumalı liste testleri önce yazılır:** junction içinden silme, bulut yer tutucusu,
  paylaşılan bileşen, launcher klasörü. Dördü de kırmızıdan yeşile geçmeden silme kodu birleşmez.
- Kaldırma testleri Windows Sandbox'ta: gerçek NSIS, Inno, MSI kurucu kur, kaldır, farkı ölç.
- **Prova kipi:** ortam değişkeniyle her silme ve kaldırma günlüğe yazılır ama yapılmaz.
- `uc` denetimi: gerçek exe penceresi %100, %125, %150; iki iddia iki kanıt.

## 10. Aşamalar

- [ ] **A0 İskelet:** git, `.gitignore` (`tmp/`), çözüm ve projeler, AGPL, `AGENTS.md`,
      teknesyum-ui bağlanması, `Theme.axaml`, kabuk standardı testleri, üst çubuk.
- [ ] **A1 Salt Okunur Tarama:** FindFirstFileEx tarayıcı, SQLite dizin, Genel Bakış, Harita,
      "büyük ve LastWrite'ı eski" listesi. Silme yok, yönetici yok. Gerçek diskte ölçüm.
- [ ] **A2 Güvenli Silme:** worker + pipe, korumalı liste ve testleri, karantina, Geri Al,
      ilk birimler: geliştirici artığı, önbellek, indirilen kurulum.
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

## Açık Konular

- teknesyum-ui A0'da `setup.js` ile bağlanır; `teknesyum-ui.json` ve token'lar gelmeden renk yazılmaz.
- Kod imzası sertifikası: satın alma sahibin kararı, A7'den önce.
