# Kullanım Sinyalleri ve Oyun Kütüphaneleri İncelemesi

Bağlam: [docs/plan.md](../plan.md) Bölüm 2 (Kullanım Sinyalleri) ve Bölüm 3 (Birimler — Toplu
Teklif). Yedi depo `gh api` / `gh repo view` ile incelendi, DERİN işaretlilerde kaynak dosyalar
doğrudan okundu (dosya:satır referanslarıyla aşağıda).

## 1. EricZimmerman/PECmd ve EricZimmerman/Prefetch — ÇOK DERİN

Prefetch **ayrıştırma mantığı** `EricZimmerman/PECmd`'de değil, ayrı kütüphane
`EricZimmerman/Prefetch`'te (`PECmd` yalnız CLI kabuğu, kütüphaneyi NuGet ile çeker).
İkisi de **MIT** lisanslı → AGPL-3.0-or-later ile doğrudan uyumlu, kaynağı kopyalayıp
projeye gömmek serbest, tek şart telif bildirimini korumak.

**Mekanizma** (`Prefetch/PrefetchFile.cs:24-56`):
- İlk 3 bayt `"MAM"` ise Windows 10/11 sıkıştırmalı dosya. Ofset 4'te sıkıştırılmamış boyut
  (uint32), ofset 8'den itibaren sıkıştırılmış veri.
- Açma `Prefetch/XpressStream/Xpress2.cs:8-45`: `ntdll.dll`'den P/Invoke ile
  `RtlDecompressBufferEx` (format `CompressionFormatXpressHuff = 4`) çağrılıyor — yani
  **Windows'a özgü, yönetilmeyen (unmanaged) çağrı**; Linux'ta derlenemez ama DustyBytes zaten
  yalnız Windows hedefliyor, sorun değil.
- Çözülen baytlarda ofset 0'da sürüm (`Version17/23/26/30or31`), ofset 4'te `"SCCA"` imzası.
  Sürüme göre ayrı sınıfa dallanıyor (`PrefetchFile.cs:57-72`).
- **Win10/11 gövdesi** (`Prefetch/Versions/Version30or31.cs`): dosya bilgi bloğu ofset 84'te
  başlıyor; `FileMetricsOffset/Count`, `TraceChainsOffset/Count`, `FilenameStringsOffset/Size`,
  `VolumesInfoOffset` alanları buradan okunuyor (satır 39-53).
  **Son 8 çalışma zamanı**: ofset 44'ten itibaren 64 bayt, 8×`Int64` FILETIME döngüsü,
  0'dan büyükse listeye ekleniyor (satır 58-70) — plandaki "son 8 çalışma zamanı" tam burada.
  **RunCount**'ta bilinen bir kayma var: yeni Windows 10 derlemeleri sayacı 8 bayt geriye
  kaydırmış, kod bunu `runcountPre == 0` kontrolüyle telafi ediyor (satır 76-86) — kırılgan bir
  nokta, üretici formatı sürüm içinde bile değişmiş.
- Dosya/dizin referansları: `VolumeInformation[].FileReferences` (MFT referans numaraları) ve
  `DirectoryNames` (satır 155-195) — "dosya/dizin referansları" sinyali burada.

**Yönetici**: `C:\Windows\Prefetch\` klasörü ACL ile SYSTEM/Administrators'a kilitli; okuma
için yönetici şart (plan bunu zaten doğru not etmiş).
**Güvenilirlik**: yalnız GUI dışı exe çalıştırmalarını değil, komut satırından başlatılanları
da yakalar ama Windows'ta Prefetch varsayılan olarak SSD'lerde bazı sistemlerde kapalı
olabilir (`EnablePrefetcher` kayıt defteri değeri 0 ise dosya hiç üretilmez) — sinyal yoksa
"bilinmiyor" kuralı burada devreye girmeli.
**Windows sürüm farkı**: XP/2003 (v17), Vista/7 (v23), 8.x/2012 (v26), 10/11 (v30/31) — dördü
de ayrı bayt düzeni, kütüphane hepsini kapsıyor.

## 2. EricZimmerman/RegistryPlugins — UserAssist eklentisi — DERİN

Depo adı planla eşleşmiyor: `EricZimmerman/UserAssist` diye ayrı bir depo yok; ayrıştırıcı
`EricZimmerman/RegistryPlugins` içinde `RegistryPlugin.UserAssist/UserAssist.cs` olarak duruyor
ve `EricZimmerman/Registry` (temel kayıt defteri okuyucu, MIT) üzerine kurulu. Lisans: **MIT**,
AGPL ile uyumlu.

**Mekanizma** (`RegistryPlugin.UserAssist/UserAssist.cs`):
- Anahtar: `Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\*\Count` (satır 27-28).
- Değer adı ROT13 ile şifreli; `Helpers.Rot13Transform` ile çözülüyor, içinde GUID varsa
  bilinen klasör adına çevriliyor (satır 78-92).
- Veri düzeni ham bayt dizisinde: ofset 4 = çalışma sayısı (satır 105), ofset 8 = son çalışma
  zamanı (FILETIME) eski biçimde. **Windows 7+ yeni biçim** (≥68 bayt): ofset 8 = odak sayısı,
  ofset 12 = odak süresi (ms), ofset 60 = son çalışma zamanı (satır 108-114) — planın "sayaç,
  son çalışma, odak süresi" maddesiyle birebir örtüşüyor.
- 1970 öncesi tarihler `null`'a çevriliyor (bozuk/boş veri filtresi, satır 117-120).

**Yönetici**: gerekmez — aktif kullanıcının `HKCU` kovanı zaten process altında açık; yalnız
**başka bir kullanıcının** `NTUSER.DAT` dosyasını canlıyken okumak için o dosya kilitli olur,
kapalıyken (oturum kapalı profil) okumak yönetici + dosya kopyalama ister.
**Tuzak**: `RegistryPluginBase`/`Registry` kütüphanesi çevrimdışı hive ayrıştırıcı; canlı
kayıt defterinden okumak için DustyBytes'ın kendi `Microsoft.Win32.Registry` çağrısı yeterli,
bu kütüphaneyi gömmeye gerek yok — yalnız **mantığı** (ofset düzeni, ROT13, biçim ayrımı)
referans almak yeterli.

## 3. JosefNemec/Playnite ve JosefNemec/PlayniteExtensions — ÇOK DERİN

Launcher eklentileri Playnite'ın kendi deposunda değil, ayrı `PlayniteExtensions` deposunda
(`source/Libraries/`). İkisi de **MIT** → AGPL ile uyumlu, kaynak doğrudan alınabilir.

| Launcher | Dosya | Mekanizma |
|---|---|---|
| Steam | `Libraries/SteamLibrary/Services/SteamLocalService.cs` | `GetLibraryFolders()` (satır 341-365) `steamapps\libraryfolders.vdf`'i **SteamKit2'nin `KeyValue` sınıfıyla** (bkz. madde 4) ayrıştırıyor; her kütüphane kökünde `steamapps\appmanifest*.acf` taranıyor (satır 133-163); `LastPlayed` alanı Unix epoch olarak okunuyor (`SteamLocalService.cs:62`: `DateTimeOffset.FromUnixTimeSeconds(app["LastPlayed"].AsLong())`) |
| Epic | `Libraries/EpicLibrary/EpicLauncher.cs:115-142` | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item` JSON dosyaları taranıyor, `InstallLocation`/`DisplayName` okunuyor |
| GOG | `Libraries/GogLibrary/Gog.cs:24-50` | Kurulu mu kontrolü kayıt defterinden: `HKLM\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths` (64-bit) / `HKLM\SOFTWARE\GOG.com\GalaxyClient\paths` |
| Ubisoft (Uplay) | `Libraries/UplayLibrary/Uplay.cs:59` | Benzer registry tabanlı kurulum tespiti (`Uplay Install\Installer\\...`) |
| Battle.net | `Libraries/BattleNetLibrary/BattleNet.cs:58` | Registry + `Product.db` protobuf dosyası |
| Xbox (Store) | `Libraries/XboxLibrary/Xbox.cs` | UWP/MSIX paket API'leri (`PackageManager`) üzerinden, registry değil |

**Önemli tespit**: Playnite, Steam dışındaki launcher'lar için **"son oynama" tarihini
launcher'ın kendi verisinden değil**, kendi oturum takibinden üretiyor — GOG/Uplay/Battle.net
için launcher tarafında güvenilir, herkese açık bir "LastPlayed" alanı yok ya da kütüphane onu
okumuyor. **DustyBytes planına etkisi**: Steam dışındaki launcher'lar için "son oynama" sinyali
zayıf olabilir; kurulum varlığı + `InstallLocation` boyutu güvenilir ama "ne zaman oynandı"
için ek kaynak (Prefetch/UserAssist ile exe'yi eşleştirme) gerekebilir.

**Yönetici**: hiçbiri gerektirmiyor — hepsi kullanıcı kovanı (`HKCU`/`HKLM` okunabilir kısım)
ve kullanıcı `%PROGRAMDATA%`/`%APPDATA%` yolları.

## 4. SteamRE/SteamKit — VDF/ACF Ayrıştırıcı

Depo doğru, ayrı bir "VDF parser" deposu yok — VDF/ACF ayrıştırıcısı SteamKit2'nin içinde
`KeyValue` sınıfı olarak duruyor ve **Playnite'ın SteamLibrary'si bunu doğrudan referans alıp
kullanıyor** (`SteamLocalService.cs` içinde `using SteamKit2;`, `new KeyValue()`). Lisans:
**LGPL-2.1**. AGPL-3.0 ile derleme zamanı bağlama (NuGet paketi, DLL referansı) sorunsuz;
**kaynağı doğrudan kopyalayıp projeye gömmek** LGPL'in "değişiklik + kaynağı açık tutma"
şartını devreye sokar — DustyBytes için önerilen yol paketi olduğu gibi NuGet bağımlılığı
olarak kullanmak, kaynağını çatallamamak.

`libraryfolders.vdf` biçimi: iç içe anahtar-değer (Valve KeyValue/VDF metni), her kütüphane
kökü nümerik alt anahtar altında `"path"` alanıyla tutuluyor (`SteamLocalService.cs:316-338`).
`appmanifest_<appid>.acf` içinde `LastPlayed` (Unix epoch, saniye) ve `SizeOnDisk` alanları var
— planın Bölüm 2 tablosundaki satırla birebir doğrulanıyor.

## 5. Heroic-Games-Launcher / legendary — Epic Manifest Biçimi

`derrod/legendary` artık **`legendary-gl/legendary`** adresine taşınmış (org değişmiş, plan
metnindeki ad güncel değil — GitHub eski adı otomatik yönlendiriyor ama kaynak orada değil).
Her ikisi de **GPL-3.0** — AGPL-3.0-or-later ile kaynak birleştirme uyumlu (FSF'nin uyumluluk
listesinde GPLv3 → AGPLv3 birleştirme açıkça izinli), ama **kaynağı doğrudan kopyalamak GPL-3.0
şartlarını (kaynak dağıtımı, aynı lisans) tetikler**; DustyBytes için önerilen yaklaşım —
Playnite'ın zaten yaptığı gibi — **yalnız `.item`/manifest biçimini referans almak**, kod
kopyalamamak (madde 3'te zaten Epic okuma mekanizması PlayniteExtensions'tan (MIT) doğrulandı,
bu ikisine ihtiyaç kalmıyor).

## 6. libyal/libscca ve MarkBaggett/srum-dump — SRUM Değer mi?

- `libyal/libscca`: Prefetch (SCCA) biçimi için **C kütüphanesi**, lisans **LGPL-3.0**.
  DustyBytes zaten C# + `EricZimmerman/Prefetch` (MIT, madde 1) kullanacağı için libscca'ya
  ihtiyaç yok; yalnız biçim referansı olarak faydalı olabilir ama gereksiz — atlanabilir.
- `MarkBaggett/srum-dump`: Python, **GPL-3.0**, SRUM (`SRUDB.dat`, ESE veritabanı) verisini
  xlsx'e döküyor. SRUM ağ/CPU kullanımı ve **uygulama başına çalışma zaman aralıklarını**
  (30 günlük pencere) tutuyor ama: (a) ESE (`.dat`) veritabanı — okumak için ayrı bir ESE
  ayrıştırıcı gerektirir (ör. `ManagedEsent`/`Esent` P/Invoke, DustyBytes'ın CsWin32 yaklaşımına
  ek karmaşıklık), (b) dosya `%SystemRoot%\System32\sru\SRUDB.dat` kilitli, canlı sistemde VSS
  gölge kopyası ya da kilit atlatma gerekir — plan zaten Amcache'i "kilitli hive, VSS ister"
  diye attı, **aynı gerekçeyle SRUM da A3 kapsamı dışında tutulmalı**: sinyal değeri var
  (30 günlük pencere Prefetch'in 8 çalıştırmasından daha zengin) ama maliyet/karmaşıklık oranı
  kötü. Öneri: plana **"Ertelenenler"** altına not düşülsün, A3'e alınmasın.

## 7. Medya Oynatıcı "Son Açılanlar" Okuyucusu

Aranan doğrudan bir "VLC/MPC-HC recent list parser" deposu yok; en yakın eşleşme
`iamkroot/trakt-scrobbler` (Python, **GPL-2.0**, VLC/Plex/MPC-HC/MPV için trakt.tv
scrobbler). **Tuzak — mekanizma plana uymuyor**: `trakt_scrobbler/player_monitors/vlc.py` ve
`mpc.py`, dosyadan/registry'den geçmiş okumuyor; VLC'nin **HTTP web arayüzünü** (`localhost` +
port, `WebInterfaceMon` taban sınıfı) canlı olarak yoklayarak "şu an ne oynuyor" bilgisini
alıyor — oynatıcı kapalıyken hiçbir veri yok. DustyBytes'ın ihtiyacı **çevrimdışı, oynatıcı
kapalıyken de okunabilen** bir "son açılanlar" listesi; bu depo o işi görmüyor.

Bilinen (ayrı doğrulama gerektirmeyen, yaygın adli bilişim literatüründeki) biçimler:
- **VLC**: `%APPDATA%\vlc\vlc-qt-interface.ini`, `[RecentsMRL]` bölümü, `list=` anahtarında
  `;` ile ayrılmış `file:///` URI listesi — düz metin INI, ayrıştırması kütüphanesiz de kolay.
- **MPC-HC/BE**: `HKCU\Software\MPC-HC\MPC HC\File Manager Position List` altında
  `MRU0000`, `MRU0001`... değerleri (dosya yolu + son oynatma konumu, ikili blob).
- Ayrıca **Windows Jump List** (`%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations`)
  zaten planın "Jump list / Recent" satırında var; VLC/MPC-HC kayıtları burada da bulunur —
  **tek bir Jump List ayrıştırıcısı (ör. gövdesi basit `.automaticDestinations-ms` OLE
  bileşik dosya okuyucusu) hem genel "son belgeler" hem de medya oynatıcı sinyalini aynı anda
  karşılayabilir**, ayrı bir VLC/MPC-HC entegrasyonuna gerek kalmayabilir.
**Sonuç**: bu madde için dış kütüphane önerilmiyor; INI/registry biçimleri düz metin/basit
ikili, DustyBytes içinde birkaç satır kodla doğrudan yazılabilir (dış bağımlılık, lisans riski
yok).

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 2. Kullanım Sinyalleri | Prefetch ayrıştırma mantığı (MAM/Xpress2 açma, sürüm dallanma, son 8 çalışma, RunCount kayması) `EricZimmerman/Prefetch`'ten referans alınsın; NuGet paket olarak da düşünülebilir (MIT) | EricZimmerman/Prefetch |
| 2. Kullanım Sinyalleri | UserAssist ofset düzeni (eski/yeni biçim, ROT13, odak süresi) `RegistryPlugins`'ten referans alınsın; canlı `HKCU` okuması DustyBytes'ın kendi kodunda, kütüphane gömülmesin | EricZimmerman/RegistryPlugins |
| 2. Kullanım Sinyalleri | Steam `LastPlayed`/`SizeOnDisk` okuma akışı (`libraryfolders.vdf` → `appmanifest*.acf`) birebir Playnite'ın akışı örnek alınsın; VDF ayrıştırma için SteamKit2 NuGet paketi (LGPL-2.1, kaynak kopyalanmadan) kullanılsın | SteamRE/SteamKit, JosefNemec/PlayniteExtensions |
| 2. Kullanım Sinyalleri | Epic/GOG/Ubisoft/Battle.net için "kurulu mu + nerede" güvenilir, "ne zaman oynandı" için launcher verisi zayıf — Prefetch/UserAssist ile exe eşleştirmesi asıl kaynak olsun | JosefNemec/PlayniteExtensions |
| 2. Kullanım Sinyalleri, Ertelenenler | SRUM sinyali (30 günlük pencere) değerli ama ESE + kilitli dosya karmaşıklığı Amcache'le aynı gerekçeyle A3 dışına, "ertelenenler"e eklensin | MarkBaggett/srum-dump |
| 2. Kullanım Sinyalleri | VLC/MPC-HC "son açılanlar" için dış kütüphane yok; INI (`vlc-qt-interface.ini`) ve registry MRU biçimleri DustyBytes içinde doğrudan yazılsın; Jump List ayrıştırıcısı ikisini de kapsayabilir, ayrı entegrasyon gereksiz olabilir | (yok — doğrudan yazım) |
| 3. Birimler | Oyun biriminin "nasıl bulunur" sütunu Playnite'ın launcher-başına-okuyucu deseniyle netleşti: her launcher ayrı dosya/registry mekanizması, tek bir soyutlama altında toplanmalı | JosefNemec/PlayniteExtensions |
