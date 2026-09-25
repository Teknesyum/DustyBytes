# Kaldırıcı Ve Kalıntı Tarayıcı İncelemesi

Kapsam: DustyBytes Bölüm 6 (Program Kaldırma ve Kalıntı) için yedi açık kaynak deponun
incelenmesi. `gh api` / `gh repo view` ile README, lisans, dizin ağacı ve kaynak dosyaları
okunmuştur; "ÇOK DERİN" ve "DERİN" işaretli depolarda asıl mekanizma dosya:satır ile
anlatılmıştır.

**Depo Adı Düzeltmesi:** Plan'da `Klocman/Bulk-Crap-Uninstaller` yazılı; bu depo
`BCUninstaller/Bulk-Crap-Uninstaller` altına taşınmış (Klocman adresi otomatik yönlendiriyor).
Doğrusu: **BCUninstaller/Bulk-Crap-Uninstaller**.

---

## 1. BCUninstaller/Bulk-Crap-Uninstaller (ÇOK DERİN)

**Lisans:** Apache License 2.0 (`Licence.txt`). AGPL-3.0-or-later ile uyumlu — Apache-2.0,
FSF'nin GPL-uyumlu listesinde (tek yönlü: Apache-2.0 kodu AGPL'e katılabilir, tersi olmaz).
DustyBytes'a kaynak/algoritma taşımak lisans açısından sorunsuz; `NOTICE`/atıf gerekir.

**Proje düzeni:** `source/UninstallTools` (çekirdek mantık, UI'dan bağımsız), `source/BulkCrapUninstaller`
(WinForms arayüz), `source/SteamHelper`, `source/StoreAppHelper` (ayrı yardımcı exe'ler),
`source/KlocTools` (MSI/registry yardımcıları).

### Kurulu program kaynakları (`source/UninstallTools/Factory/ApplicationUninstallerFactory.cs:24-`)

`GetUninstallerEntries` sırayla toplar:
- **MSI:** `MsiTools.MsiEnumProducts()` (satır 38) — tüm MSI ürünlerini GUID ile numaralandırır.
- **Registry:** `RegistryFactory` (satır 58) — `HKLM/HKCU\...\Uninstall` (WOW6432Node dahil),
  MSI GUID listesiyle çapraz kontrol edilir.
- **Sürücü taraması:** `DirectoryFactory` (satır 90) — registry'de bulunmayan, disktte kalan
  kurulum klasörlerini registry sonuçlarına göre arar (kalıntı programları da bulabilir).
- **Bağımsız fabrikalar** (ayrı thread'de, `ConcurrentApplicationFactory` satır 28):
  `GetMiscUninstallerEntries` (satır 253) reflection ile `IIndependantUninstallerFactory`
  arayüzünü uygulayan her sınıfı bulur — Steam (`SteamFactory.cs`), Windows Store/MSIX
  (`StoreAppFactory.cs`), Chocolatey (`ChocolateyFactory.cs`).
- Sonuçlar `MergeResults` ile birleştirilip yinelenenler ayıklanır.

**Store/MSIX kaldırma** (`StoreAppFactory.cs:21-60`): Ayrı `StoreAppHelper.exe` süreci
`/query` ile paketleri listeler, `Remove-AppxPackage -package <FullName> -confirm:$false`
komutu üretir (satır 24). Kayıttaki `IsProtected` bayrağı (satır 53) korumalı sistem
paketlerini işaretler — plandaki "MSIX/Store paketi PackageManager ile" adımına doğrudan
emsal.

**Sessiz kaldırma** (`PredefinedAppQuietUninstallStringGenerator.cs`): Genel akış
`QuietUninstallString`/`UninstallString` alanlarını registry'den okur; MSI için
`msiexec /x {GUID} /qn`; NSIS/Inno Setup için `NsisQuietUninstallStringGenerator.cs` ve
`InnoSetupQuietUninstallStringGenerator.cs` sessiz bayrağı (`/S`, `/VERYSILENT`) otomatik
ekler. Bilinen özel durumlar (ör. Edge beta `--force-uninstall`) elle kodlanmış
(`PredefinedAppQuietUninstallStringGenerator.cs:19-23`) — DustyBytes bunu ölçekli
tutmamalı, plan zaten üreticinin `QuietUninstallString`'ini önceliklendiriyor.

### Kalıntı arama ve güven puanlama (asıl mekanizma)

`source/UninstallTools/Junk/` altında üç katman:

1. **Bulucular** (`Junk/Finders/`): `Drive/CommonDriveJunkScanner.cs`, `InstallLocationScanner.cs`,
   `PrefetchScanner.cs`, `WerScanner.cs`; `Registry/UninstallerKeySearcher.cs`,
   `SoftwareRegKeyScanner.cs`, `ComScanner.cs`, `FirewallRuleScanner.cs`,
   `UserAssistScanner.cs` vb. Her biri `JunkCreatorBase` (`Junk/Finders/JunkCreatorBase.cs`)
   soyut sınıfından türer.
2. **Sonuç kapları** (`Junk/Containers/`): `FileSystemJunk.cs`, `RegistryKeyJunk.cs`,
   `RegistryValueJunk.cs`, `RunProcessJunk.cs`, `StartupJunkNode.cs`.
3. **Güven puanlama** (`Junk/Confidence/`): Her aday bir `ConfidenceRecord` listesi taşır;
   toplam puan `ConfidenceLevel` eşiğine (Bad=5, Questionable=7, Good=9, VeryGood=12)
   karşılaştırılır (`ConfidenceLevel.cs:12-17`).

**İki çapa mantığı somut karşılığı** — `ConfidenceGenerators.GenerateConfidence`
(`Junk/Confidence/ConfidenceGenerators.cs:21-53`):
- Ad benzerliği Sift4 string-mesafe algoritmasıyla ölçülür (`MatchStringToProductName`,
  satır 60-98); kısa adlar (≤4 karakter) hiç eşleştirilmez — DustyBytes planındaki
  "Tools/Update/Client gibi genel ad parçaları eşleşme sayılmaz" kuralının doğrudan emsali.
- `ConfidenceRecords.cs`: her kanıt ağırlıklı puan taşır — `IsUninstallerRegistryKey +20`,
  `ExplicitConnection +4`, `CompanyNameMatch +4`, ama `DirectoryStillUsed -7`
  (dizin başka bir uygulama tarafından hâlâ kullanılıyorsa), `IsStoreApp -10`,
  `CompanyNameDidNotMatch -2`. Yani **tek kanıt** (örn. sadece ad benzerliği +2) eşiği
  geçemez; en az iki bağımsız işaret (ad eşleşmesi + registry anahtarı ya da yayıncı
  eşleşmesi) gerekir — plandaki "iki çapa kuralı"nın neredeyse birebir örneği.
- `CheckIfDirIsStillUsed` (`JunkCreatorBase.cs:41-45`): bir klasörün başka bir kurulu
  uygulamanın `InstallLocation`'ı altında olup olmadığını kontrol eder — paylaşılan
  bileşen koruması burada.
- `TestForSimilarNames` (`ConfidenceGenerators.cs:120-142`): "AppX Extended" ve "AppX" gibi
  benzer adlı uygulamaların kalıntılarını karıştırmaması için, en iyi eşleşme dışındakilere
  ceza puanı verir.

**Sertifika eşleştirme** (`Factory/InfoAdders/CertificateGetter.cs:14-36`): Önce yürütülebilir
dosyalardan (`SortedExecutables`, ilk 2 dosya), sonra MSI ise `MsiTools.GetCertificate`,
son çare uninstaller exe'sinden Authenticode sertifikası okunur (`X509Certificate2`) —
plandaki "Authenticode imzalayan" çapasının kaynağı.

**MSI desteği:** `KlocTools/IO/MsiTools.cs` + `KlocTools/Native/MsiWrapper.cs` —
`MsiEnumProducts`, bileşen numaralandırma; plandaki `MsiEnumComponents` fikriyle örtüşüyor.

**Geri alma:** `BulkCrapUninstaller/Functions/Tools/SystemRestore.cs:20-50` — kaldırma
öncesi `SysRestore.StartRestore` ile Windows Sistem Geri Yükleme noktası oluşturur (kullanıcıya
sorar, `displayMessage=false` ile zorunlu da yapılabilir). **Dosya bazında karantina/geri alma
yok** — BCU yalnız sistem geri yükleme noktasına güveniyor, DustyBytes'ın planladığı
birim-başına-karantina modeli BCU'dan daha güvenli bir yaklaşım.

**Bilinen hatalar / issue'lar (yanlış silme):**
- **#611** "PROGRAM GONE ROGUE! Deletes all files in downloads directory" — Downloads
  klasörünün kökü yanlışlıkla kurulum klasörü olarak eşleşip temizlenmiş.
- **#751 / #753** "mistakenly deleted download folder" — osu!lazer kaldırılırken Downloads
  klasörünün tamamı silinmiş (aynı desen, tekrarlayan).
- **#504** "BEWARE Portable Apps Deleted" — taşınabilir (kurulumsuz) uygulamalar drive-scan
  tarafından yanlış eşleştirilip silinmiş.
- **#734** "Almost had to deal with irreparable damage" — kullanıcı son anda fark etmiş.
- **#758** "Removed Shared Settings Folder for Multiple Blender Versions" — paylaşılan
  ayar klasörü, sürümler arası ayrım yapılmadan silinmiş.
- **#724** "Marks all wrong items with Good confidence, all right items with Bad confidence" —
  güven puanlama algoritmasının kendisi ters çalışabiliyor; doğrulama hep gerekli.
- **#947** dry-run/`--dry-run` bayrağı isteniyor — henüz yok; DustyBytes'ın plandaki "Prova
  kipi" (Bölüm 9) tam bunu kapatıyor.

**Ortak desen:** Tüm ciddi yanlış-silme şikâyetleri "geniş kapsamlı klasör" (Downloads kökü,
paylaşılan ayar klasörü, taşınabilir uygulama klasörü) etrafında toplanıyor — tam olarak
DustyBytes planının "iki çapa" ve "Downloads içinde indirilen kurulum → karantina, doğrudan
silme değil" ayrımıyla önlemeyi hedeflediği sınıf.

### DustyBytes Planına Somut Etkisi

- Bölüm 6 madde 5-6 (kimlikle eşleme, iki çapa) BCU'nun `ConfidenceRecord` ağırlıklı toplama
  modeliyle doğrudan örtüşüyor; DustyBytes ağırlıkları BCU'nunkinden ilham alabilir ama
  **dosya-sistemi kararlarını asla tek confidence sayısına bırakmamalı** — BCU'nun #724 ve
  Downloads-silme vakaları, tek bir toplam puanın yeterli olmadığını gösteriyor. DustyBytes'ın
  planındaki üç katman (Yüksek görünür / Orta işaretsiz / Düşük gizli) BCU'dan bir adım ileri.
- `CheckIfDirIsStillUsed` mantığı (başka kurulu programın `InstallLocation`'ı altındaki
  dizinleri hariç tutma) plandaki "paylaşılan yayıncı anahtarında yalnız ürün alt anahtarına
  dokunulur" kuralına doğrudan uygulanabilir bir teknik.
- BCU'da dosya bazlı karantina yok, yalnız sistem geri yükleme noktası var — DustyBytes'ın
  planladığı birim-başına karantina, BCU'nun en büyük zayıflığını (#611, #751, #753, #504)
  kapatan tasarım kararı; plan bu haliyle korunmalı.
- `--dry-run` eksikliği (#947) DustyBytes'ın Bölüm 9 "Prova kipi"ni doğruluyor — üretime
  girmeden önce zorunlu tutulmalı.

---

## 2. microsoft/winget-cli (DERİN)

**Lisans:** MIT. AGPL-3.0-or-later ile tam uyumlu, kod alıntısı serbest (telif bildirimi korunur).

**ARP eşleme:** `src/AppInstallerRepositoryCore/Microsoft/ARPHelper.h:20` — tarama kökü
`SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`, `ARPHelper.cpp:137-145`'te HKCU/HKLM
ve 32/64-bit (WOW6432Node) dörtlü kombinasyon taranıyor. Alan haritası (`ARPHelper.h:22-60`):
`DisplayName`, `Publisher`, `DisplayVersion`, `InstallLocation`, `UninstallString`,
`QuietUninstallString`, `WindowsInstaller`, `SystemComponent`, `NoModify`.
`ARPCorrelation.h:426-444` boş/bozuk `DisplayName` kayıtlarını eliyor.

**Kalıntı tespiti:** Yok — winget yalnız `UninstallString`'i çalıştırır, dosya sistemi
taraması yapmaz. Issue **#1117** "Leftover cleanup option after uninstallation" hâlâ açık;
resmi Microsoft aracı bile bunu çözmemiş — DustyBytes'ın buradaki boşluğu doldurma fırsatı.

**Koruma/geri alma:** Ayrı korumalı liste yok (`SystemComponent=1` sadece gizler), rollback
yok. Issue **#6160** (PATH temizlenmiyor), **#5527** (user-scope klasör yanlış tespiti).

**Plana etkisi:** ARP alan haritası ve dörtlü hive tarama şeması (HKCU/HKLM ×
32/64-bit) doğrudan DustyBytes'ın Bölüm 6 madde 8 "Taranan yerler" listesine şablon.

## 3. ScoopInstaller/Scoop

**Lisans:** Unlicense veya MIT (kullanıcı tercihi, dual license) — AGPL ile tam uyumlu.

**Temiz kaldırma modeli:** Her sürüm `apps\<ad>\<sürüm>` altında izole kurulur;
`app\current` bir **dizin junction**'dır (`lib/install.ps1:238-257`, `link_current`),
salt-okunur işaretlenir (`attrib +R /L`) — yanlışlıkla silinmeye karşı. Kaldırma sırası
(`libexec/scoop-uninstall.ps1:60-136`): `pre_uninstall` hook → çalışan süreç kontrolü →
manifest uninstaller → shim silme → kısayol silme → junction kaldırma
(`attrib -R /L` + `Remove-Item`) → PATH/env geri alma → versiyon klasörü silme.
`persist\<app>` (kullanıcı verisi/config) **varsayılan olarak korunur**, yalnız `-p/--purge`
bayrağıyla silinir.

**Kalıntı tespiti / koruma:** Registry'ye normalde hiç yazılmadığından "kalıntı" sorunu
yapısal olarak küçük; ayrı bir tarayıcı yok. `persist_dir` kavramı fiilen "korunan bölge".
**Geri alma:** Yok. Issue **#6654** dosya ilişkilendirmeleri kaldırmada temizlenmiyor,
**#6025** cache temizliği eksik.

**Plana etkisi:** "current" junction + izole sürüm klasörü + varsayılan olarak silinmeyen
`persist` deseni, DustyBytes'ın "korumalı liste" ve "kullanıcı verisi karantina dışı
tutulur mu" kararına doğrudan örnek — ama plan zaten karantina modeliyle bunu aşıyor.

## 4. chocolatey/choco — AutoUninstaller (DERİN)

**Lisans:** Apache-2.0 (kök `LICENSE` dosyasından doğrulandı; GitHub API "Other" gösteriyor
ama içerik Apache-2.0). AGPL ile uyumlu, atıf gerekir.

**ARP tarama:** `src/chocolatey/infrastructure.app/services/RegistryService.cs:44-46` —
`SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` + `...\Installer\UserData` (MSI).
Satır 79-86: HKCU/HKLM × Registry32/Registry64 dört kombinasyon ayrı taranıyor.
`UpdateSnapshot` (satır 150-230): DisplayName, UninstallString, QuietUninstallString,
InstallLocation, Publisher, SystemComponent, WindowsInstaller, NoRemove/NoModify/NoRepair
okunuyor; installer tipi (Msi/InnoSetup/Nsis/InstallShield/Custom) imza deseniyle tahmin
ediliyor. `GetMsiInformation` (satır 274-330): MSI GUID'i ters çevirip UserData altında
`InstallProperties` okuyor (compressed-GUID çözümü).

**Kalıntı tespiti:** Yok — yalnız kurulum öncesi/sonrası **registry snapshot farkı**
(`GetInstallerKeysChanged`, satır 520-524) kendi paketlediği yazılımın hangi ARP anahtarını
eklediğini bulmak için; genel dosya sistemi taraması yapmıyor.

**Koruma mekanizması:** `AutomaticUninstallerService.cs:47` — paket içinde
`.skipAutoUninstall` dosyası varsa otomatik kaldırma tamamen atlanır. Satır 141-146:
InstallLocation ya da registry anahtarı zaten yoksa "başka yolla kaldırılmış" deyip atlar
(çifte kaldırma önlenir). Satır 216-236: installer tipi tespit edilemez ve sessiz kaldırma
stringi yoksa, interaktif "Uninstall may not be silent, proceed?" sorusu (varsayılan "no").

**Geri alma:** Yok. Issue **#3319** "Scratch folder still left", **#2583** "Leftover
autostart key" — küçük ama gerçek kalıntı örnekleri.

**Plana etkisi:** GUID ters-çevirme (compressed GUID → MSI UserData) tekniği ve installer
tipi tahmini (Msi/InnoSetup/Nsis/InstallShield imza desenleri) Bölüm 6 madde 3'teki
"üreticinin kaldırıcısı" tespitine doğrudan uygulanabilir; `.skipAutoUninstall` deseni
DustyBytes'ın korumalı listesine "elle işaretlenmiş istisna" fikri olarak eklenebilir.

## 5. ChrisTitusTech/winutil

**Lisans:** MIT.

**Model:** ARP taraması **yok** — `config/applications.json`'da statik whitelist (winget/choco
paket ID'leri, `content`, `foss` alanları). `functions/public/Invoke-WPFUnInstall.ps1:1-80`
işaretli uygulamaları `winget`/`choco` üzerinden kaldırır. AppX ayrı akış:
`functions/private/Remove-WinUtilAPPX.ps1:19-30` — `Get-AppxPackage "*$Name*" -AllUsers`
**joker karakterli isim eşleşmesi** (tehlikeli — yanlış pakete çarpabilir);
`Remove-WinUtilProvisionedAPPX.ps1:24-45` DISM tabanlı, PowerShell 5.1 alt-process olarak
çağrılıyor (PS7 uyumsuzluğu notu satır 34-35'te).

**Kalıntı tespiti / koruma listesi:** Yok. Koruma yalnız "whitelist'te olmayan programa
dokunulmaz" felsefesiyle dolaylı sağlanıyor — choco'nun AutoUninstaller'ının tam tersi.

**Geri alma:** `config/tweaks.json` içinde `WPFTweaksRestorePoint` — **opt-in, elle
tetiklenen**, kaldırma akışına otomatik bağlı değil.

**Issue:** **#1339** "MicroWin wrongly removed Windows Defender" — joker/whitelist tabanlı
debloat modülünde yanlış bileşen kaldırma örneği.

**Plana etkisi:** Joker karakterli AppX eşleştirmesi (`"*$Name*"`) DustyBytes'ın planındaki
"kimlikle eşle, adla değil" kuralının (Bölüm 6 madde 5) neden zorunlu olduğunun negatif
örneği — tam olarak kaçınılması gereken desen.

## 6. Raphire/Win11Debloat

**Lisans:** MIT.

**Kaynak:** `Config/Apps.json` — her paket için `AppId`, `Recommendation`
(safe/optional/unsafe), `RemovalMethod` (Appx/WinGet); 86 "safe", 48 "optional", 7 "unsafe"
(Microsoft Edge, Get Help, Microsoft Store, Windows Terminal, Xbox TCUI Framework, Xbox
Identity Provider, Xbox Speech To Text). Kaldırma: `Scripts/AppRemoval/Remove-SelectedApps.ps1`
(`Remove-WinGetApp` / `Remove-AppxApp`, `Get-AppxPackage`/`Remove-AppxPackage` +
`Remove-ProvisionedAppxPackage`, tüm kullanıcılar).

**Koruma:** Gerçek "asla dokunma" listesi yok — `Scripts/Helpers/Confirm-UnsafeAppRemoval.ps1`
yalnız `Microsoft.WindowsStore` ve `Microsoft.WindowsTerminal` için ekstra onay kutusu
gösteriyor. **Bilinen hata — Issue #650:** onay penceresini X/Alt+F4 ile kapatmak bile
uygulamayı siliyor (onay bypass edilebiliyor).

**Geri alma:** Uygulama kaldırma için yok; yalnız registry/tweak tarafında
`Backup-RegistryState.ps1`/`Restore-RegistryBackup.ps1` var, kaldırmaya bağlı değil.

**Issue:** **#606** "broke everything", **#576** "lost my restore points", **#540**
"restore point not created", **#734** "uninstall fails 0x800706FD (domain-joined)".

**Plana etkisi:** Üç kademeli risk etiketi (safe/optional/unsafe) DustyBytes'ın "Yüksek/Orta/
Düşük güven katmanı" (Bölüm 6 madde 9) fikrine benziyor, ama Win11Debloat'ta bu katman
**bypass edilebilir onay diyaloguna** dayanıyor — DustyBytes'ın onayı UI seviyesinde değil
worker'da (korumalı liste, kod tarafında) zorunlu kılması bu hatayı önler.

## 7. farag2/Sophia-Script-for-Windows

**Lisans:** MIT.

**Kaynak:** `src/Sophia_Script_for_Windows_11/Module/Sophia.psm1` (10549 satır) —
`Get-AppxBundle` (~satır 8047) `Get-AppxPackage -PackageTypeFilter Bundle -AllUsers` ile
canlı sistemden okuma, manifest taraması değil.

**"Korumalı" liste yerine "hariç tutulan" liste:** `$ExcludedAppxPackages` (satır
7873-~7940) — Dolby Access, AMD Radeon Software, Intel Graphics Control Panel, ELAN
Touchpad, Microsoft.DesktopAppInstaller, Microsoft.StorePurchaseApp,
Microsoft.WindowsNotepad, Microsoft.WindowsStore, Microsoft.WindowsTerminal,
Microsoft.WindowsSubsystemForLinux, HEVC/AV1/Raw Image Extension. Bu liste **silinemez**
anlamına gelmiyor, yalnız kaldırma ekranında **gösterilmiyor** — mantık DustyBytes'ın
korumalı listesinin tersi yönde (gizleme, engelleme değil).

**Geri alma:** Yok, kod tabanında bulunamadı.

**Issue:** **#731** "Defender broken or removed", **#561** "Task Scheduler broken or
removed", **#554** "Get-WindowsEdition broken" — doğrudan yanlış-silme şikâyeti değil,
ama sonradan bozulan sistem bileşenlerine işaret ediyor (sistem paketi kaldırmanın yan
etkisi).

**Plana etkisi:** `$ExcludedAppxPackages` listesindeki 10+ isim (WindowsTerminal,
DesktopAppInstaller, StorePurchaseApp, WindowsSubsystemForLinux, codec uzantıları)
DustyBytes'ın Bölüm 5 "Korumalı liste" tablosuna "sistem bütünlüğü için asla dokunulmayacak
Appx paketleri" alt listesi olarak somut aday sağlıyor.

---

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 6 (madde 6, iki çapa) | Ağırlıklı güven puanlama tablosu (`+20` registry anahtarı, `-7` dizin hâlâ kullanımda, `-10` Store app) BCU'daki `ConfidenceRecords`'tan uyarlanabilir; ama tek toplam puana güvenmemek için üç katman (Yüksek/Orta/Düşük) korunmalı | BCUninstaller/Bulk-Crap-Uninstaller |
| 6 (madde 7, paylaşılan yayıncı) | `CheckIfDirIsStillUsed` mantığı — bir klasörün başka kurulu programın `InstallLocation`'ı altında olup olmadığını kontrol et | BCUninstaller/Bulk-Crap-Uninstaller |
| 5 (Karantina) | Dosya-bazlı karantina eksikliği (yalnız sistem restore point) BCU'nun #611/#751/#753/#504 (Downloads klasörü, taşınabilir uygulama) vakalarının kök nedeni; DustyBytes'ın birim-başına karantina kararı bu sınıfı kapatıyor | BCUninstaller/Bulk-Crap-Uninstaller |
| 9 (Prova kipi) | `--dry-run` eksikliği community'de açık istek (#947); DustyBytes'ın plandaki prova kipi zorunlu tutulmalı | BCUninstaller/Bulk-Crap-Uninstaller |
| 6 (madde 8, taranan yerler) | ARP alan haritası + HKCU/HKLM × 32/64-bit dörtlü tarama şeması şablon olarak alınabilir | microsoft/winget-cli, chocolatey/choco |
| 6 (madde 3, üreticinin kaldırıcısı) | Installer tipi tahmini (Msi/InnoSetup/Nsis/InstallShield imza desenleri) ve MSI compressed-GUID çözümleme tekniği | chocolatey/choco |
| 5 (Korumalı liste) | `.skipAutoUninstall` deseni — elle işaretlenmiş istisna dosyası fikri | chocolatey/choco |
| 5 (Korumalı liste) | `$ExcludedAppxPackages` listesindeki somut paket adları (WindowsTerminal, DesktopAppInstaller, StorePurchaseApp, WSL, codec uzantıları) doğrudan alt liste adayı | farag2/Sophia-Script-for-Windows |
| 6 (madde 5, kimlikle eşleme) | Joker karakterli AppX eşleştirmesi (`"*$Name*"`) kaçınılması gereken **negatif örnek**; kimlik her zaman PackageFullName ile | ChrisTitusTech/winutil |
| 6 (madde 9, güven katmanı) | Onay diyaloğunun UI'da bypass edilebilmesi (#650) — onayı worker/kod seviyesinde zorunlu kıl, UI'da değil | Raphire/Win11Debloat |
| 5 (Karantina, kullanıcı verisi) | `persist/` deseni: kullanıcı verisi varsayılan olarak silinmez, ayrı açık onay gerektirir | ScoopInstaller/Scoop |
| Genel (leftover tarama boşluğu) | Hiçbir paket yöneticisi (winget, choco, scoop) gerçek bir dosya-sistemi kalıntı taraması sunmuyor — DustyBytes'ın bu alandaki tek gerçek rakibi BCUninstaller; fark yaratma fırsatı burada | microsoft/winget-cli, ScoopInstaller/Scoop |
