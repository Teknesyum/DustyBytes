# Temiz Kaldırma Araştırması

Tarih: 2026-09-28. Kapsam: `src/DustyBytes.Clean/Uninstall/*` ve arayüzdeki kaldırma sayfası
(`UninstallViewModel.cs`, `ProgramsViewModel.cs`, `AppBackend.cs`). Kod değiştirilmedi.

Hedef cümle sahibinden: "Program kalıntısız kaldırılsın; kayıt anahtarı ya da başka iz kalmasın."

Web araştırmasının ham çıktısı (İngilizce, olduğu gibi): [kaldirma-arastirmasi-girdi.md](kaldirma-arastirmasi-girdi.md).

---

## Bölüm 1: Bugünkü Akış

### Arayüz Tarafı

Programlar sayfasında "Kaldır" yeni bir `UninstallViewModel` sayfası açar (`ProgramsViewModel.cs:127`).
Sayfa açılınca yetkisiz arayüz süreci kendi içinde bir **önizleme taraması** yapar
(`AppBackend.PreviewLeftoversAsync`, `AppBackend.cs:181`). Bu yalnız bilgi amaçlıdır.

Kullanıcı "Programı kaldır" der, bir onay kutusu çıkar (`UninstallViewModel.cs:224`). Ardından
`Ops.Uninstall` isteği worker'a gider. Geri yükleme noktası kurulamazsa ikinci bir onay kutusu
"Noktasız devam et" ister (`:270`).

Kaldırıcı bitince kalıntı listesi gelir. **Yüksek** güvenliler işaretli, **Orta** işaretsiz,
**Düşük** olanlar "Daha fazla" düğmesinin arkasında gizli (`:185-192`). Kullanıcı "Kalıntıları sil"
der, üçüncü onay kutusu çıkar (`:309`), `Ops.RemoveLeftovers` worker'a gider.

Yani en iyi durumda kalıntısız kaldırma **en az dört tık** ister: Kaldır, onay, Kalıntıları sil, onay.

### Worker Akışı Adım Adım

1. **Kapılar** (`UninstallHandlers.HandleUninstall`): `UserApproved` şart; program listesi yeniden
   okunur; korumalı çalışma zamanları (VC++, .NET vb. — `ProtectedList.RuntimeReason`) ve korumalı
   Store paketleri reddedilir; `NoRemove=1` olan program reddedilir.
2. **Geri yükleme noktası**: `SRSetRestorePointW`, tür `APPLICATION_UNINSTALL` (`RestorePoint.cs`).
   Kaldırıcı çalıştıysa nokta tamamlanır, çalışmadıysa iptal edilir.
3. **Önceki anlık görüntü**: `LeftoverScanner.Snapshot` kaldırıcıdan *önce* çalışır; bütün adaylar
   o an toplanır ve kanıtları (exe sürüm bilgisi, imza) program henüz yerindeyken okunur.
4. **Üreticinin kaldırıcısı** (`Uninstaller.BuildCommand`, `Uninstaller.cs:36-51`):
   - MSI: `msiexec.exe /x {ProductCode} /qb /norestart` — sessiz sayılır, temel ilerleme penceresi görünür.
   - `QuietUninstallString` varsa o kullanılır.
   - Yoksa `UninstallString` **görünür** çalışır; kullanıcı üreticinin sihirbazına tıklamak zorundadır.
   - MSIX: `PackageManager.RemovePackageAsync`, yalnız geçerli kullanıcı için.
   - Süreç bir iş nesnesine (Job Object) konur; alt süreçler bitene dek beklenir (`ProcessTree.cs`).
     NSIS'in `Au_.exe` kopyası ve Inno'nun geçici kopyası böylece yakalanır.
   - Çıkış kodu: 0, 3010/1641 (yeniden başlatma), 1605 (zaten yok) başarı; 1602, 1618 başarısız.
5. **Fark** (`LeftoverScanner.Diff`): Program hâlâ kurulu görünüyorsa (Uninstall anahtarı ve kaldırıcı
   exe'si duruyorsa ya da MSI ürünü kayıtlıysa) **hiçbir kalıntı önerilmez**. Değilse önceki listeden
   hâlâ var olanlar kalır; dosya adayları korumalı liste kapısından yeniden geçer.
6. **Kalıntı silme** (`LeftoverRemover.Remove`): yalnız kullanıcının işaretlediği kimlikler.
   - Kayıt türleri (anahtar, değer, servis, başlangıç, ilişki, güvenlik duvarı) silinmeden önce **tek bir
     `.reg` dosyasına** dışa aktarılır: `%AppData%\DustyBytes\registry-backup\<tarih>-<ad>-<id>.reg`.
     Yedek yazılamazsa o kayıt girdisi silinmez.
   - Klasör, dosya, kısayol **karantinaya** taşınır (`WorkerBindings.QuarantineLeftover`).
   - Servis: `DeleteService` (önce durdurmaz; çalışıyorsa yeniden başlatmada gider).
   - Görev: XML dosyası yedek klasörüne kopyalanır, `schtasks /Delete /F`.
   - Güvenlik duvarı: `netsh advfirewall firewall delete rule name=… program=…`.

### Kalıntı Kapsamı

| Alan | Taranıyor mu | Nerede | Not |
|---|---|---|---|
| Kurulum klasörü | Evet | `AddInstallFolder` | `InstallLocation` yoksa kaldırıcı konumundan çıkarılır |
| AppData Roaming/Local/LocalLow, ProgramData, Program Files (x86), `Local\Programs` | Evet | `ScanDataFolders` | İki düzey: `Yayıncı\Ürün` |
| HKLM\SOFTWARE (64 ve WOW6432Node), HKCU\Software | Evet | `ScanSoftwareKeys` | İki düzey; `Classes`, `Microsoft`, `Windows`, `Policies`, `WOW6432Node` atlanır |
| Kendi Uninstall anahtarı | Evet | `AddUninstallKey` | Her zaman Yüksek |
| Servisler | Kısmen | `ScanServices` | Yalnız `ImagePath` kurulum klasöründeyse; sürücüler kaçar |
| Zamanlanmış görevler | Evet | `ScanTasks` | `System32\Tasks` XML'i, komut kurulum klasöründeyse |
| Run / RunOnce | Evet | `ScanRunKeys` | HKLM 64/32 ve HKCU; `StartupApproved` değerleri kalır |
| Başlat menüsü, Başlangıç klasörü, masaüstü kısayolları | Evet | `ScanShortcuts` | Görev çubuğu sabitlemeleri (Quick Launch\User Pinned) yok |
| Dosya ilişkileri | Kısmen | `ScanAssociations` | Yalnız ProgID anahtarı; `.ext` varsayılanı, `OpenWithProgids` değerleri, HKCU `FileExts` kalır |
| Güvenlik duvarı | Evet | `ScanFirewall` | Yalnız `App=` alanı |
| COM (CLSID, TypeLib, Interface, AppID) | **Hayır** | — | `Classes` atlanıyor, `SOFTWARE\Classes\CLSID` açıkça korumalı (`LeftoverScanner.cs:505`) |
| Kabuk uzantıları (ContextMenuHandlers, Approved) | **Hayır** | — | |
| App Paths | **Hayır** | — | `SOFTWARE\Microsoft\Windows` altında, korumalı sayılıyor |
| MSI kayıtları (Installer\Products, UserData, UpgradeCodes, Components, Folders) | **Hayır** | — | Yalnız "hâlâ kurulu mu" sorusu için okunuyor |
| SharedDLLs sayaçları | **Hayır** | — | |
| EventLog kaynakları | **Hayır** | — | |
| AppCompatFlags, MuiCache, UserAssist, RegisteredApplications | **Hayır** | — | |
| Diğer kullanıcıların hive'ları ve AppData'ları | **Hayır** | — | Yalnız worker'ı çalıştıran hesabın HKCU'su ve klasörleri |
| WER, CrashDumps, Prefetch, Package Cache | **Hayır** | — | Package Cache bilinçli olarak dışlanıyor |
| Program Files'ta sahipsiz klasörler | **Hayır** | — | |

### Güven Seviyeleri

Puan kanıtların toplamıdır (`Confidence.cs`). Eşikler: **Yüksek ≥ 16**, **Orta ≥ 8**. Ek kural:
**en az iki bağımsız çapa** (kurulum klasörü, sürüm kaynağı, imza, yayıncı, ad, Uninstall anahtarı,
kurulum tarihi) yoksa puan ne olursa olsun Düşük.

Artılar: kurulum klasörünün kendisi +20, çıkarılmış klasör +14, klasör içinde / klasörü gösteren değer
+12, Uninstall anahtarı +20, imza +8 (doğrulanmamışsa +5), CompanyName/ProductName +6, tam ad +6,
kısmi ad +3, yayıncı +4, kurulum günü +3.

Eksiler: başka şirket −4, başka program aynı adla eşleşiyor −6 (ve ad çapası düşer), Store paketi −10.

Engeller (puandan bağımsız, listeye hiç girmez, "Engellendi" olarak döner): kullanıcı klasörleri,
çok geniş yollar, korumalı liste, yayıncı düzeyi klasör/anahtar, paylaşılan yayıncılar (Microsoft,
Adobe, NVIDIA, Google, Intel, AMD) altında tam ad dışı, başka kurulu programın klasörüyle çakışma,
içinde exe olan ve kurulum kaydı olmayan (taşınabilir sanılan) klasör, sistem anahtarları.

Bu yapı BCU'nunkinden **daha sıkı** ve iyi: iki çapa kuralı, paylaşılan anahtarları silme riskini
düşürüyor. Eksik olan güvenlik değil, **kapsam** ve **akış**.

### Otomatik Olan, Kullanıcıya Kalan

Hiçbir kalıntı otomatik silinmiyor. Yüksek olanlar yalnız önceden işaretli geliyor.

Orta olanları kullanıcı tek tek işaretlemek zorunda; Düşük olanları görmek için önce "Daha fazla".

### Açıklar

1. **Sessiz kaldırma yarım.** `InstallerDetector` Inno, NSIS, InstallShield'i tanıyor ama
   `BuildCommand` bunu kullanmıyor. `QuietUninstallString` yazmayan Inno/NSIS programları (çoğunluk)
   görünür sihirbazla kalkıyor; kullanıcı "İleri, Evet, Bitir" tıklıyor ve sihirbazın "ayarları da
   sil?" sorusuna yanlış cevap verebiliyor.
2. **Kalıntı silme ayrı bir adım.** Kaldırıcı başarıyla bitse bile Yüksek güvenli kalıntılar için
   ikinci istek ve onay gerekiyor. "Kalıntısız" beklentisiyle çelişen en görünür nokta bu.
3. **Kurulum klasörü bilinmezse taramanın yarısı susuyor.** Servis, görev, Run, kısayol, ilişki,
   güvenlik duvarı taramalarının hepsi `InstallDir is null` ise hemen dönüyor. `InstallLocation`
   boş, kaldırıcısı `Package Cache` ya da `InstallShield Installation Information` altında olan
   programlarda bu alanlarda hiç aday çıkmıyor ve kullanıcıya not düşülmüyor.
4. **Kayıt defterinde kalan bilinen izler:** COM sınıfları ve tip kütüphaneleri, kabuk uzantıları,
   App Paths, `.ext` varsayılanı ve `OpenWithProgids` değerleri, HKCU `FileExts`, `StartupApproved`,
   EventLog kaynağı, MSI yetim kayıtları, `Installer\Folders`, AppCompatFlags, MuiCache,
   RegisteredApplications. Bunların bir kısmı (COM, kabuk uzantısı) Explorer'da hata ve yavaşlık üretir.
5. **Yalnız bir kullanıcı.** HKCU ve AppData, worker'ı çalıştıran hesabınki. Standart kullanıcı
   başka bir yönetici hesabıyla yükseltiyorsa (omuz üstü UAC) worker yanlış kullanıcının HKCU'suna
   ve AppData'sına bakar; arayüzdeki önizleme doğru kullanıcıyı gösterir, gerçek tarama başkasını.
   Diğer profillerin `NTUSER.DAT` hive'ları hiç yüklenmiyor.
6. **Kaldırıcı sonrası yeni tarama yok.** `Diff` yalnız önceki listeyi süzüyor. Kaldırıcının kendisinin
   bıraktığı log klasörü, kaldırma sırasında oluşan anahtar ya da önceki taramada eşleşmeyen iz
   bulunmuyor.
7. **Kaldırıcı bozuksa çıkış yok.** Kaldırma komutu yoksa `CanUninstall=false`, düğme kapalı. Kaldırıcı
   exe'si silinmiş ama Uninstall anahtarı duran "hayalet" girdiler temizlenemiyor. Kaldırıcı iptal
   edilirse ya da program kurulu görünmeye devam ederse kalıntıların hiçbiri önerilmiyor.
8. **Servis önce durdurulmuyor, sürücüler kapsam dışı.** Çalışan servisin klasörü kilitli kalıyor,
   karantina başarısız oluyor; yeniden başlatmada taşıma (MoveFileEx) yok.
9. **SharedDLLs gözetilmiyor.** Kurulum klasöründe başka bir programla paylaşılan (sayacı > 1) DLL
   varsa klasör yine Yüksek çıkabilir; sayaç da düşürülmüyor.
10. **Yedekten geri dönüş arayüzde yok.** `.reg` ve görev XML'i diske yazılıyor ama geri yükleme düğmesi
    yok; kullanıcı dosyayı bulup çift tıklamak zorunda.
11. **Güvenlik duvarı kuralı ada göre siliniyor.** `netsh … name=X program=Y` aynı adlı başka kuralları
    da götürebilir; elde kural kimliği (`ValueName`) varken kullanılmıyor.
12. **MSIX yalnız geçerli kullanıcıdan kalkıyor.** Tüm kullanıcılar ve hazırlanmış (provisioned) kopya
    kalıyor; yeni kullanıcıda yeniden beliriyor.
13. **Geri yükleme noktası sessizce atlanabilir.** Windows 24 saatte bir nokta sınırı uyguluyor
    (`SystemRestorePointCreationFrequency`); çağrı başarılı dönüp yeni nokta oluşturmayabilir. Sıra
    numarasının gerçekten yeni olduğu doğrulanmıyor. (Doğrulanmalı; bkz. kaynaklar.)

---

## Bölüm 2: Sektör Uygulaması

### Bulk Crap Uninstaller (Açık Kaynak)

BCU'nun kalıntı motoru `source/UninstallTools/Junk/` altında. `JunkManager` her `IJunkCreator`
tarayıcısını yansıma ile bulur, program başına çalıştırır, sonuçları birleştirip güven puanlar.
([JunkManager.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/JunkManager.cs),
[klasör listesi](https://api.github.com/repos/BCUninstaller/Bulk-Crap-Uninstaller/contents/source/UninstallTools/Junk))

Tarayıcılar ve DustyBytes karşılığı:

| BCU tarayıcısı | Ne tarar | DustyBytes |
|---|---|---|
| `ProgramFilesOrphans` | Hiçbir kurulu programa ait olmayan Program Files klasörleri | Yok |
| `InstallLocationScanner`, `UninstallerLocationScanner` | Kurulum klasörü ve geride kalan kaldırıcı exe | Var |
| `PrefetchScanner` | `%WINDIR%\Prefetch\*.pf` | Yok |
| `WerScanner` | Windows Hata Raporlama klasörleri | Yok |
| `ShortcutJunk` | Başlat menüsü ve masaüstü (kullanıcı + ortak), `.lnk` hedefi | Var |
| `StartupJunk` | Run anahtarları ve Başlangıç klasörü | Var |
| `ComScanner` | CLSID `InprocServer32`/`LocalServer32`, TypeLib, Interface, ProgID, `ShellEx`, `PersistentHandler`, `OpenWithProgIDs`, 32/64 bit | **Yok** |
| `EventLogScanner` | `Services\EventLog\Application\<kaynak>`; `EventMessageFile` kurulum klasöründeyse | **Yok** |
| `FirewallRuleScanner` | `FirewallRules` değerlerindeki `App=` | Var |
| `InstallerFoldersScanner` | `Installer\Folders`; başka program da kullanıyorsa ceza | **Yok** |
| `UninstallerKeySearcher` | `Classes\Installer\Products/Features/Patches`, `Installer\UserData`, UpgradeCode | **Yok** |
| `AppCompatFlagScanner`, `AudioPolicyConfigScanner`, `UserAssistScanner`, `TracingScanner`, `HeapLeakDetectionScanner`, `DebugTracingScanner` | Explorer ve sistemin exe yoluna göre tuttuğu kayıtlar | **Yok** |
| `RegisteredApplicationsFinder` | Varsayılan programlar kaydı | **Yok** |
| `SoftwareRegKeyScanner` | `SOFTWARE\Yayıncı\Ürün` | Var |

**Güven puanı.** Kalemler toplanır, 0 orta noktadır; bantlar Bad=5, Questionable=7, Good=9, VeryGood=12
([ConfidenceLevel.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/Confidence/ConfidenceLevel.cs)).
Seçilmiş kalemler: Uninstall anahtarı +20, açık bağlantı (yol eşleşmesi) +4, şirket adı +4, boş klasör +4,
Store uygulaması −10, klasör başka programca kullanılıyor −7, içinde exe var −4, yayıncı hâlâ kullanımda −4,
ürün adı hâlâ kullanımda −4, benzer adlı başka program −2
([ConfidenceRecords.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/Confidence/ConfidenceRecords.cs)).
Ad benzerliği Sift4 uzaklığıyla ölçülür; en iyi eşleşme dışındakiler cezalanır
([ConfidenceGenerators.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/Confidence/ConfidenceGenerators.cs)).

DustyBytes'ın farkı: BCU'da exe içeren klasör −4 ceza alır, DustyBytes'ta tamamen engellenir. BCU'da
iki bağımsız kanıt kuralı yoktur. DustyBytes bu iki noktada daha temkinli.

**Silme güvenliği.** Dosyalar Geri Dönüşüm Kutusu'na gider
([FileSystemJunk.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/Containers/FileSystemJunk.cs)).
Kayıt anahtarı `DeleteSubKeyTree` ile silinir; `.reg` yedeği ayrı bir çağrıdır, arayüz sorar
([RegistryKeyJunk.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Junk/Containers/RegistryKeyJunk.cs)).
DustyBytes burada da önde: yedek zorunlu, yedeksiz silme yok.

**Sessiz kaldırma.** BCU esas olarak `QuietUninstallString`'i kullanır, MSI için kendi yolu vardır
([RegistryFactory.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Factory/RegistryFactory.cs),
[MsiUninstallModes.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Uninstaller/MsiUninstallModes.cs)).
Kaldırıcı türlerini 14 sınıfta tanır
([UninstallerType.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/UninstallerType.cs)).
Alt süreçleri izler, MSI için sistemdeki bütün `msiexec`'leri bekler, takılan süreci CPU/GÇ sayaçlarıyla
yakalar ([BulkUninstallEntry.cs](https://raw.githubusercontent.com/BCUninstaller/Bulk-Crap-Uninstaller/master/source/UninstallTools/Uninstaller/BulkUninstallEntry.cs)).

**Liste kaynakları.** Kayıt defteri, Steam, Store, Scoop, Chocolatey, Windows özellikleri ve
`DirectoryFactory` (kaydı olmayan sahipsiz klasörleri sahte program olarak listeler)
([Factory klasörü](https://api.github.com/repos/BCUninstaller/Bulk-Crap-Uninstaller/contents/source/UninstallTools/Factory)).

### Revo Uninstaller

Üç tarama kipi: Güvenli, Orta (önerilen), Gelişmiş. Gelişmiş daha çok yanlış pozitif getirir, karar
kullanıcıdadır ([el kitabı](https://www.revouninstaller.com/online-manual/uninstaller/)).

Kalıntı listesinde **kalın** öğeler programın gerçekten oluşturduğu izlenenlerdir; kalın olmayanlar
bağlam içindir ve işaretlense bile silinmez. Kırmızı öğeler "asla silme" demektir (aynı kaynak).

**İzleme (Traced Programs):** Kurulumu izleyerek çalıştırır, her dosya ve kayıt yazımını günlüğe alır;
kaldırmada tahmin yerine gerçek kayıt kullanılır
([nasıl yapılır](https://www.revouninstaller.com/how-to/trace-a-program-with-revo-uninstaller-pro/)).

**Zorla kaldırma (Forced Uninstall):** Kaldırıcısı bozuk ya da yok olan programın izlerini doğrudan
temizler ([nasıl yapılır](https://www.revouninstaller.com/how-to/use-forced-uninstall/)).

Varsayılan olarak geri yükleme noktası, isteğe bağlı tam kayıt yedeği alır.

### Geek Uninstaller Ve IObit Uninstaller

Geek önce üreticinin kaldırıcısını çalıştırır, sonra bilinen yerleri tarar, kullanıcı istemediğini
kaldırır; bozuk kaldırıcı için "Force Removal" vardır
([ghacks](https://www.ghacks.net/2016/06/22/geek-uninstaller-remove-windows-apps/)).

IObit'te "Powerful Scan" derin kalıntı taraması, "Force Uninstall+" bozuk girdiler içindir
([IObit](https://www.iobit.com/en/pressroom-iobit-uninstaller-13--more-powerful-on-program-uninstall-and-leftover-removal-640.php),
[yardım](https://www.iobit.com/product-manuals/iu-help/)).

### Windows İç Yapısı: Temiz Kaldırmanın Dokunması Gerekenler

**Uninstall anahtarları.** HKLM, HKLM\WOW6432Node ve HKCU
([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)).

**MSI.** Sessiz kaldırma `msiexec /x {ProductCode} /qn`
([Advanced Installer](https://www.advancedinstaller.com/how-to-uninstall-msi-package.html)).
Ürün ayrıca `HKCR\Installer\Products\<paketlenmiş GUID>`,
`Installer\UserData\<SID>\Products`, `UpgradeCodes`, `UserData\S-1-5-18\Components` altında tutulur;
bunlar kalırsa Windows Installer ürünü kurulu sanar
([paketlenmiş GUID ve sayım](https://installpac.wordpress.com/2008/03/31/packed-guids-darwin-descriptors-and-windows-installer-reference-counting/)).
Normalde msiexec bunları kendisi temizler; yetim kayıt ancak MSI kaldırması yarım kaldığında olur.

**SharedDLLs.** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs` yol → sayaç tutar.
Kaldırmada sayaç düşürülür, dosya ancak sıfırda silinir; erken silme diğer programı bozar
([Revenera](https://www.revenera.com/blog/software-installation/cleaning-up-your-shared-dlls-registry-references-for-msis/)).

**App Paths.** `App Paths\<exe>` Çalıştır kutusunun exe'yi bulmasını sağlar; kalırsa silinmiş dosyayı
gösterir ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration)).

**Dosya ilişkileri ve COM.** `HKCR\.ext` → ProgID, `OpenWithProgids`, kullanıcı düzeyinde
`Explorer\FileExts\.ext` ve `UserChoice`
([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/fa-progids)).
COM: `CLSID\{guid}\InprocServer32`/`LocalServer32`, `TypeLib`, `Interface\…\ProxyStubClsid32`, `AppID`;
32 bit olanlar `WOW6432Node\Classes` altında. Yetim `InprocServer32` "sınıf kayıtlı değil" hatası üretir.

**Kabuk uzantıları.** `HKCR\<tür>\shellex\ContextMenuHandlers\<ad>` → CLSID; ayrıca "Approved"
listesi ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/reg-shell-exts),
[Old New Thing](https://devblogs.microsoft.com/oldnewthing/20151208-00)).

**Servis ve sürücü.** `sc delete` ya da `DeleteService`; anahtar `Services\<ad>`
([Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-delete)).

**Başlangıç.** Run/RunOnce ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys));
Görev Yöneticisi'ndeki açık/kapalı durumu ayrıca `Explorer\StartupApproved\Run`, `Run32`,
`StartupFolder` altında ikili değer olarak tutulur. Run değeri silinip bu kalırsa bayat girdi kalır.

**Görevler.** `schtasks /Delete /TN … /F`
([Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-delete)).

**Güvenlik duvarı.** `FirewallRules` değerleri `|App=…|` içerir; silme `INetFwPolicy2.Rules` ya da
`netsh` ile ([Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/netsh-advfirewall-firewall-control-firewall-behavior)).

**Explorer önbellekleri.** `MuiCache` (exe açıklaması), `AppCompatFlags\Layers` (uyumluluk ayarı),
`UserAssist` (çalıştırma sayacı). Zararsız ama iz bırakır.

**Diğer kullanıcılar.** Oturumu kapalı kullanıcının `NTUSER.DAT` dosyası `RegLoadKey` ile geçici
yüklenir, düzenlenir, boşaltılır; açık oturumunki zaten `HKU\<SID>` altındadır
([PDQ](https://www.pdq.com/blog/modify-the-registry-of-another-user/)).

**MSIX.** Bütün kullanıcılardan kaldırma `Remove-AppxPackage -AllUsers`, yeni kullanıcılara
dağıtılmasını durdurma `Remove-AppxProvisionedPackage`; ikisi ayrı işlerdir
([Remove-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage),
[Remove-AppxProvisionedPackage](https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage)).

**Kilitli dosyalar.** `MoveFileExW(…, MOVEFILE_DELAY_UNTIL_REBOOT)` işlemi
`PendingFileRenameOperations` kuyruğuna yazar; hedef verilirse yeniden başlatmada **taşır**
([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw)).

**Kurulum aracına göre sessiz bayraklar.**
- Inno Setup: `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`
  ([jrsoftware](https://jrsoftware.org/ishelp/topic_uninstcmdline.htm)).
- NSIS: `uninst.exe /S`; beklemek için `_?=<kurulum klasörü>`
  ([NSIS](https://nsis.sourceforge.io/Reference/SilentUnInstall)). DustyBytes iş nesnesiyle beklediği
  için `_?=` gerekmez; `_?=` kaldırıcının kendi exe'sini silememesine yol açar.
- InstallShield (InstallScript): yanıt dosyası (`.iss`) ister; genelde sessiz yapılamaz
  ([Revenera](https://docs.revenera.com/installshield26helplib/helplibrary/SilentUninstall.htm)).

**Geri yükleme noktası sıklığı.** Windows 8'den beri 24 saat içinde ikinci nokta varsayılan olarak
oluşturulmaz ([CreateRestorePoint](https://learn.microsoft.com/en-us/windows/win32/sr/createrestorepoint-systemrestore)).
`SRSetRestorePointW` için aynı davranış doğrulanmalı.

### İzleme Ve Sonradan Tarama

İzleme (Revo) gerçeği kaydeder, yanlış pozitif azdır. Ama yalnız izlenerek kurulan programlarda işe
yarar, her kurulumda maliyeti vardır, kurulumdan sonra servislerin yazdıklarını kaçırır.

Sonradan tarama (BCU, Geek, IObit, DustyBytes) her programda çalışır ama olasılıksaldır; bu yüzden
herkes güven puanı ve kullanıcı onayı kullanır.

### Ortak Güvenlik Kuralları

- Microsoft/Windows anahtarları, ortak dosya türlerinin sınıfları, VC++ ve .NET çalışma zamanlarına dokunma.
- Silmeden önce geri yükleme noktası ve kayıt yedeği.
- Dosyaları kalıcı silme; Geri Dönüşüm Kutusu ya da karantina.
- "Kesin bu programın" ile "benziyor"u ayır; düşük güvenliyi kullanıcı işaretlemeden silme.
- Önce üreticinin sessiz yolunu dene, bozuksa zorla kaldırmaya geç.

DustyBytes bu beş kuralın beşine de bugün uyuyor.

---

## DustyBytes İçin Öneriler

Öncelik, sahibinin cümlesine ("kalıntı kalmasın") katkı ve tık sayısına göre. Her madde bir açığa bağlı.

1. **Tek adımda temizlik** (Açık 2). Kaldırıcı başarılı ve program gerçekten gitmişse Yüksek güvenli
   kalıntılar aynı worker isteğinde karantina + `.reg` yedeğiyle kaldırılsın; Orta/Düşük liste olarak
   kalsın. Varsayılan açık bir seçenek: "Kesin kalıntıları hemen temizle".
   Dosyalar: `UninstallHandlers.cs` (`HandleUninstall` sonunda `LeftoverRemover.Remove`, yeni bayrak
   sabiti), `UninstallViewModel.cs` (seçenek, tek onay metni), `RemovalTests.cs`.
2. **Kurulum aracına göre sessiz kaldırma** (Açık 1). `BuildCommand` `InstallerType`'ı kullansın:
   Inno → `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`, NSIS → `/S`, MSI → `/qn` ya da `/qb` +
   `REBOOT=ReallySuppress`. InstallShield ve bilinmeyen görünür kalsın. Sessiz deneme başarısızsa
   görünür kaldırıcıya düşülsün. Dosyalar: `Uninstaller.cs`, `InstallerDetector.cs`, `MiscTests.cs`.
3. **COM ve kabuk uzantıları** (Açık 4). `HKLM\SOFTWARE\Classes` (64 ve 32) ve `HKCU\Software\Classes`
   altında `CLSID`, `TypeLib`, `Interface`, `AppID`; sunucu yolu (`InprocServer32`, `LocalServer32`,
   `TypeLib\…\win32|win64`) kurulum klasöründeyse aday. Bunlara bağlı `shellex\ContextMenuHandlers`
   ve `Shell Extensions\Approved` değerleri de. `IsProtectedKey` yalnız "sunucusu kurulum klasöründe
   olan GUID" için gevşesin; kanıt olarak yol eşleşmesi + exe kimliği (iki çapa) şart kalsın.
   Dosyalar: `LeftoverScanner.cs` (yeni `ScanCom`, `ScanShellExtensions`; büyürse `LeftoverScanner.Com.cs`
   partial), `Confidence.cs`, `ScannerTests.cs`.
4. **Değer düzeyinde kayıt temizliği** (Açık 4). `App Paths\<exe>`, `.ext` varsayılanı ve
   `OpenWithProgids` değerleri, HKCU `Explorer\FileExts\.ext\OpenWithProgids`, `StartupApproved\Run|Run32|StartupFolder`,
   `Services\EventLog\Application\<kaynak>` (`EventMessageFile` klasörde), `RegisteredApplications`,
   `AppCompatFlags\Layers` ve `Compatibility Assistant\Store`, `MuiCache`. Hepsi exe yolu kurulum
   klasöründeyse. `LeftoverKind.RegistryValue` zaten var, yedek ve silme yolu hazır.
   Dosyalar: `LeftoverScanner.cs`, `LeftoverRemover.cs` (değişiklik gerekmeyebilir), `ScannerTests.cs`.
5. **Kurulum klasörü bilinmiyorsa başka kaynak** (Açık 3). `DisplayIcon`, `App Paths`, MSI
   `Components` (ürünün bileşen yolları), servis/görev adları yayıncıyla eşleşiyorsa. Bulunamazsa
   notlara "Servis, görev, başlangıç ve ilişki taraması yapılamadı" yazılsın.
   Dosyalar: `LeftoverScanner.ResolveInstallDir`, `InstalledPrograms.cs`.
6. **Kaldırma sonrası ikinci tarama** (Açık 6). `Diff` önceki listeyi süzdükten sonra aynı kurulum
   klasörü ve kanıtlarla `Snapshot`'ı bir kez daha çalıştırıp yeni adayları birleştirsin.
   Dosyalar: `LeftoverScanner.Diff`, `Uninstaller.Diff`.
7. **Doğru kullanıcı ve diğer profiller** (Açık 5). Arayüz isteğe kullanıcının SID'ini ve profil
   yolunu koysun; worker HKCU yerine `HKU\<SID>`, AppData yerine o profili tarasın. İsteğe bağlı:
   oturumu kapalı profillerin `NTUSER.DAT`'ı `RegLoadKey` ile yüklensin (Orta güven, işaretsiz).
   Dosyalar: `ScanContext.ForSystem`, `WindowsRegistryView.cs`, `IRegistryView.cs` (`RegHive.Users`),
   `WorkerRequest` (Core.Ipc), `UninstallViewModel.cs`.
8. **Zorla kaldırma** (Açık 7). Kaldırıcı exe'si yoksa ya da kaldırıcı çalıştı ama anahtar kaldıysa
   "Zorla kaldır": üretici adımı atlanır, anlık görüntü fark sayılır, Uninstall anahtarı dahil liste
   gösterilir. Dosyalar: `UninstallHandlers.cs`, `InstalledProgram.cs` (`CanUninstall`),
   `LeftoverScanner.IsStillInstalled`, `ProgramsViewModel.cs`.
9. **Servisi durdur, kilitliyi yeniden başlatmada karantinaya taşı** (Açık 8). `DeleteService` öncesi
   `ControlService(SERVICE_CONTROL_STOP)`; karantina kilit yüzünden başarısızsa `MoveFileEx` ile
   **karantina klasörüne** (silme değil, taşıma) yeniden başlatmada. Sürücü türündeki servisler
   (Type 1/2) Orta güvenle, asla önceden işaretsiz. Dosyalar: `SystemActions.cs`, `LeftoverRemover.cs`,
   karantina deposu.
10. **SharedDLLs koruması** (Açık 9). Aday klasörde `SharedDLLs` sayacı > 1 olan dosya varsa ceza ya da
    engel; karantinaya alınan dosyaların sayacı 1 ise değer silinsin, fazlaysa düşürülsün.
    Dosyalar: `LeftoverScanner.AddFolder`, `Confidence.cs` (`SharedDllInUse`), `LeftoverRemover.cs`.
11. **MSI yetim kayıtları** (Açık 4). msiexec 1605 dönmüş ya da program kalktığı halde
    `Installer\Products`, `UserData\…\Products`, `UpgradeCodes`, `Features` duruyorsa, paketlenmiş GUID
    ile eşleşenler Yüksek aday. `MsiGuid.Compress` zaten var. Dosyalar: `LeftoverScanner.cs`, `MsiGuid.cs`.
12. **Güvenlik duvarını kimlikle sil** (Açık 11). Elde `FirewallRules` değer adı (kural kimliği) var;
    silme `INetFwPolicy2.Rules` üzerinden o kimlikle yapılsın, ada göre toplu silme bırakılsın.
    Dosya: `SystemActions.DeleteFirewallRule`.
13. **Yedekten geri al** (Açık 10). Kalıntı raporunda "Geri al": `.reg` dosyası worker'da içe aktarılır,
    görev XML'i `schtasks /Create /XML` ile döner. Dosyalar: `UninstallHandlers.cs` (yeni işlem),
    `UninstallViewModel.cs` ya da karantina sayfası.
14. **Dosya kapsamı** (Açık 4). Görev çubuğu ve Başlat sabitlemeleri
    (`AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned`), WER ve `CrashDumps`,
    isteğe bağlı Prefetch; Program Files'ta sahipsiz klasörler ayrı bir "yetim klasörler" listesi.
    Dosyalar: `ScanContext.ForSystem`, `LeftoverScanner.ScanShortcuts`, `ScanDataFolders`.
15. **MSIX tüm kullanıcılar** (Açık 12). `RemovePackageOptions.RemoveForAllUsers` ve isteğe bağlı
    `DeprovisionPackageForAllUsersAsync`. Dosya: `MsixPackages.cs`.
16. **Geri yükleme noktasını doğrula** (Açık 13). Dönen sıra numarası son noktanınkiyle aynıysa
    kullanıcıya "Bugün zaten bir nokta var, yenisi oluşturulmadı" denilsin. Dosya: `RestorePoint.cs`.
17. **Kurulum izleme** (uzun vade). Revo tarzı izleme, DustyBytes'ın kapsamını büyütür; bugünkü
    sonradan tarama + iki çapa kuralı yeterli. Yol haritasına not olarak.

İlk dört madde sahibin beklentisinin büyük kısmını karşılar: tık sayısı dörtten ikiye iner,
sihirbaz tıklaması çoğu programda kalkar, Explorer'da iz bırakan COM ve değer kalıntıları kapsanır.
