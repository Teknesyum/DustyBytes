# Danışma 003: Danışma: DustyBytes'ta Kalıntısız Kaldırma

claude-fable-5-1, 11.327 çıktı token, 179 sn. Girdi: [003-fable-dustybytes-kalintisiz-kaldirma-girdi.md](003-fable-dustybytes-kalintisiz-kaldirma-girdi.md). Cevap olduğu gibi:

---

## Danışma 003 — Cevap

### 1. "Anahtar bırakmadan" hedefinin güvenli tanımı

Üç kova; ölçü güven düzeyi **artı kanıt türü**. Yüksek zaten ≥16 puan ve iki çapa istiyor, ona bir şart daha: **yol kanıtı** (kurulum klasörü içi/klasörü gösteren değer, kendi Uninstall anahtarı, imza+sürüm eşleşmesi). Yalnız ad+yayıncı+tarihle Yüksek'e ulaşan hiçbir şey tek tıkın içine girmez.

- **Onaysız, tek tıkın içinde (Yüksek + yol kanıtı, üretici kaldırıcısı başarılı ve `IsStillInstalled=false`):** kendi Uninstall anahtarı; `SOFTWARE\Yayıncı\Ürün` anahtarı (yayıncı anahtarı yalnız boş kaldıysa, o da yedeğe yazılıp); kurulum klasörü ve AppData/ProgramData ürün klasörleri (karantina, 7 gün); Run/RunOnce değeri ve `StartupApproved` eşi; `App Paths\<exe>`; servis (`ImagePath` klasörde, Type 16/32); görev; kısayollar; güvenlik duvarı kuralı (kimlikle); EventLog kaynağı; COM CLSID/TypeLib/Interface/AppID (sunucu yolu klasörde) ve ona bağlı `shellex` girdisi + `Approved` değeri; MSI yetim `Products/UpgradeCodes` (paketlenmiş GUID eşleşir). Hepsi `.reg` yedeği zorunlu, yedek yazılamazsa silinmez — bugünkü kural.
- **Gösterilir, işaretsiz (Orta):** yalnız adla eşleşen anahtar/klasör; `.ext` varsayılanı ve `OpenWithProgids`/`FileExts` değerleri (kullanıcı başka programa yeniden atayacak, o yüzden görsün); sürücü servisleri (Type 1/2); diğer profillerin hive ve AppData'sı; içinde `SharedDLLs` sayacı >1 dosya bulunan klasör; Explorer önbellekleri (MuiCache, UserAssist, AppCompatFlags — zararsız, Düşük ve gizli kalabilir).
- **Hiç dokunulmaz (engel):** içinde başka ürün olan yayıncı anahtarı; paylaşılan yayıncılar (Microsoft, Adobe, NVIDIA…) altında tam ad dışı; `Classes\.ext` anahtarının kendisi (yalnız ProgID'yi gösteren değer temizlenir); `Microsoft`, `Windows`, `Policies`, korumalı çalışma zamanları; `Installer\Folders` ve `SharedDLLs` sayacı >1 olan her şey (yalnız sayaç düşürülür); `Approved` altında başka yerden hâlâ referanslı CLSID.

Bir seçenek kutusu, işaretsiz: **"Ayarları koru"** (AppData ürün klasörlerini listede bırakır). Varsayılan kalıntısız.
Dosyalar: `src/DustyBytes.Clean/Uninstall/Confidence.cs` (yol kanıtı bayrağı, `AutoRemovable` özelliği), `LeftoverModel.cs`, `LeftoverScanner.cs` (engel listesi), `tests/DustyBytes.Uninstall.Tests/ScannerTests.cs`.

### 2. Sıra ve tık hedefi

Hedef **2 tık**: Kaldır → tek onay kutusu → bitti. Onay kutusu bir cümlede ne olacağını sayar (geri yükleme noktası, sessiz kaldırıcı, kesin kalıntılar karantinaya, `.reg` yedeği) ve içinde "Kesin kalıntıları hemen temizle" işaretli kutu var. 1 tık olmaz: kaldırma geri alınamaz (geri yükleme noktası yeniden kurulum değildir) ve 24 saat sınırı yüzünden nokta sessizce atlanabilir; oyun karantinasından farkı bu. Bitti ekranında Orta/Düşük liste kalır, "Temizle" isteğe bağlı üçüncü tık, ayrı onay yok (zaten işaretleme onaydır).

Sıra, her adım tek başına yayınlanabilir:

1. Tek adımda temizlik (öneri 1) — `UninstallHandlers.cs` (`HandleUninstall` sonunda `LeftoverRemover.Remove`, yalnız `AutoRemovable`), `UninstallViewModel.cs` (onay metni, üçüncü onayı kaldır), `Core/Ipc/Messages.cs` (`AutoClean` bayrağı), `RemovalTests.cs`.
2. Araca göre sessiz kaldırma (öneri 2) — aşağıda, madde 3.
3. Kaldırma sonrası ikinci `Snapshot` (öneri 6) — ucuz, kapsamı hemen büyütür — `LeftoverScanner.Diff`, `Uninstaller.cs`.
4. Değer düzeyi kayıt temizliği (öneri 4) — `LeftoverScanner.cs`, `ScannerTests.cs`; `LeftoverRemover` hazır.
5. COM ve kabuk uzantıları (öneri 3) — `LeftoverScanner.Com.cs` (partial, yeni), `Confidence.cs`, `ScannerTests.cs`. Explorer'da hata üreten tek kalıntı türü bu; 4'ten sonra çünkü daha riskli.
6. Kurulum klasörü bilinmiyorsa başka kaynak + not (öneri 5) — `LeftoverScanner.ResolveInstallDir`, `InstalledPrograms.cs`.
7. Doğru kullanıcı (öneri 7) — madde 4.
8. Zorla kaldır (öneri 8), servis durdur + yeniden başlatmada karantina (9), MSI yetimleri (11), firewall kimlikle (12).
9. SharedDLLs (10), geri yükleme noktası doğrulama (16), MSIX tüm kullanıcılar (15), dosya kapsamı (14), yedekten geri al (13). İzleme (17) yol haritası notu.

İlk üç madde 1 dalga, 4-7 ikinci dalga; `docs/plan.md`'ye dalga olarak.

### 3. Sessiz kaldırma

`BuildCommand` `InstallerType`'a göre komut üretsin; sıra: `QuietUninstallString` → araca göre bayrak → görünür.

- MSI: `msiexec /x {code} /qn /norestart REBOOT=ReallySuppress /l*v <tmp>\<code>.log`; ilerlemeyi biz gösteririz, `/qb` kalksın.
- Inno (`unins000.exe` + Uninstall anahtarında `Inno Setup: App Path`): `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
- NSIS (`uninst.exe`, exe'de NSIS imzası): `/S`; `_?=` verilmez, iş nesnesi zaten bekliyor.
- WiX Burn paketleri (Electron/.NET uygulamalarında yaygın): `/uninstall /quiet /norestart`. Squirrel (`Update.exe --uninstall`): `-s`. Bu ikisi `InstallerDetector`'a eklenecek.
- InstallShield ve bilinmeyen: görünür kalır; `.iss` yanıt dosyası üretmeye kalkışılmaz.

Sihirbazı otomatik ilerletme (SendKeys/UI Automation) **yapılmasın**: dil, düğme sırası ve "ayarları da sil?" sorusu programa göre değişir; yanlış tık geri alınamaz. Yerine: sessiz deneme, zaman aşımı (10 dk; BCU gibi CPU/GÇ sayacı durgunsa "takıldı"), çıkış kodu kötü ya da program hâlâ kuruluysa **görünür kaldırıcıya düş**.

Görünür kaldırıcıda kullanıcıya bir kart: "Üreticinin kaldırıcısı açıldı, sihirbazı siz bitirin; biz bekliyoruz. Sorarsa ayar ve verilerin silinmesini seçin." Altta İptal. Bitince akış kendi devam eder; program hâlâ kuruluysa "Zorla kaldır" düğmesi çıkar.
Dosyalar: `Uninstaller.cs` (`BuildCommand` → `SilentCommand`/`VisibleCommand`), `InstallerDetector.cs`, `UninstallHandlers.cs` (deneme-düşme döngüsü, log yolu), `UninstallViewModel.cs` + `UninstallView.axaml` (kart), `MiscTests.cs`.

### 4. Yükseltme hesabı ≠ oturum hesabı

Kaynağı istek değil, boru istemcisi olsun: worker `GetNamedPipeClientProcessId` → `OpenProcessToken` → `TokenUser` SID'i ve `GetUserProfileDirectory`. İstekten gelen SID'e güvenilmez (yalnız testlerde `ScanContext` doğrudan verilir). Böylece arayüz kim ise tarama onun.

- Kayıt: `HKCU\Software` yerine `HKU\<SID>\Software`, `HKCU\Software\Classes` yerine `HKU\<SID>_Classes`. `IRegistryView`'a `RegHive.Users` + SID öneki; `WindowsRegistryView` `Registry.Users` açar. Hive yüklü değilse (`HKU\<SID>` yok) geri düş: `RegLoadKey` ile `NTUSER.DAT`/`UsrClass.dat`, iş bitince `RegUnLoadKey`.
- Klasörler: `Environment.GetFolderPath` yerine profil yolundan türetilir (`Roaming/Local/LocalLow`, Başlat menüsü, Masaüstü, Başlangıç, `User Pinned`). `ScanContext.ForSystem` bir `UserScope(Sid, ProfilePath)` alır.
- Diğer profiller: `ProfileList` altındaki SID'ler, oturumu kapalı olanların hive'ı geçici yüklenir; adaylar **Orta, işaretsiz**, kart başlığında hangi kullanıcı olduğu yazar.
- Önizleme (arayüzde yetkisiz) ile gerçek tarama aynı `UserScope`'u kullanır; ikisi ayrışmaz.

Dosyalar: `ScanContext.cs`, `IRegistryView.cs`, `WindowsRegistryView.cs`, `src/DustyBytes.Worker/WorkerServer.cs` (istemci SID), `UninstallHandlers.cs`, `AppBackend.cs` (önizleme), `tests/DustyBytes.Uninstall.Tests/Fakes.cs`.

### 5. "Bitti" ölçüsü

Deney seti, her kurulum aracından biri ve her kalıntı türünden biri:

- **7-Zip (MSI)** — kabuk uzantısı COM, dosya ilişkileri, `App Paths`.
- **VLC (NSIS)** — çok dosya ilişkisi, güvenlik duvarı kuralı, `RegisteredApplications`.
- **Notepad++ (NSIS)** — bağlam menüsü kabuk uzantısı, `OpenWithProgids`.
- **WinMerge (Inno)** — kabuk uzantısı DLL, Uninstall'da `Inno Setup:` değerleri.
- **Servis kuran bir program** (ör. Everything ya da Syncthing hizmet paketi) — servis durdurma, Run + `StartupApproved`, görev.
- **MSIX**: Windows Terminal `.msixbundle` (GitHub) — `RemoveForAllUsers`.
- **Negatif test**: aynı yayıncıdan iki ürün, Oracle VirtualBox + Java (ikisi de "Oracle Corporation"); Java kalkar, VirtualBox ve `SOFTWARE\Oracle` dokunulmamış kalır.

Kurulum: Hyper-V ya da Windows Sandbox'ta temiz Windows 11 anlık görüntüsü. Üç anlık görüntü: **A** kurulum öncesi, **B** kurulum sonrası, **C** DustyBytes kaldırması sonrası. Her anlık görüntü aynı betikle: `reg export` ile `HKLM\SOFTWARE`, `HKLM\SYSTEM\CurrentControlSet\Services`, `HKU\<SID>`, `HKU\<SID>_Classes`; dosya listesi Program Files/ProgramData/profil (projedeki MFT/USN okuyucu var, onu kullan); `schtasks /Query /XML`; `netsh advfirewall firewall show rule name=all`; `sc query`; `Get-AppxPackage -AllUsers`.

Ölçü: **(B−A) ∩ C** = programın yazıp bizim bırakadığımız iz. Gürültü listesi ayrı dosyada (Defender, Windows Update, `Explorer` önbellekleri, `Prefetch`); gürültü düşüldükten sonra hedef **0 anahtar, 0 değer, 0 dosya**. Yanına iki sayı daha: tık sayısı ve süre. Negatif testte ölçü tersi: (B−A) dışında dokunulan hiçbir şey olmayacak, yani **C−B'de VirtualBox'a ait fark 0**.

Otomasyon: ortam değişkeniyle kapılı gerçek makine testi (`DUSTYBYTES_REAL_UNINSTALL=1`), sürekli entegrasyonda koşmaz, elle koşulur; sonuç `docs/olcum/kaldirma-<tarih>.md`'ye tablo olarak, ham `reg` farkları yanına.
Dosyalar: `tools/uninstall-bench/snapshot.ps1` ve `diff.ps1` (yeni), `tests/DustyBytes.Uninstall.Tests/RealMachineUninstallTests.cs` (yeni; kalıbı `tests/DustyBytes.Rules.Tests/RealMachinePreviewTests.cs`), `docs/olcum/kaldirma-gurultu.md` (gürültü listesi).

Yok
