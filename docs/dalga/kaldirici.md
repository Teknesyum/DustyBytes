# A4 Kaldırıcı Ve Kalıntı Motoru

Program kaldırma akışı ve kimliğe dayalı kalıntı bulucu. Arayüz yok; worker'a bağlanacak iki işleyici hazır.
Bağlayıcı belgeler: `docs/dalga/ortak-kurallar.md`, plan bölüm 6, `docs/inceleme/03-kaldiricilar.md`.

## Dosyalar

`src/DustyBytes.Clean/Uninstall/` (27 dosya, yaklaşık 3000 satır):

| Dosya | İş |
|---|---|
| `IRegistryView.cs`, `WindowsRegistryView.cs` | Sahte kayıt defterine izin veren soyutlama, `RegKeyRef` (32 bit HKLM için WOW6432Node fiziksel yolu) |
| `InstalledProgram.cs`, `InstalledPrograms.cs` | HKLM/HKCU × 64/32 Uninstall okuma, çift kaydı ayıklama, gizleme kuralları, MSI UserData'dan InstallLocation |
| `MsixPackages.cs` | PackageManager ile listeleme ve yalnız tam `PackageFullName` ile kaldırma (joker reddi) |
| `MsiGuid.cs` | Ürün kodu ↔ paketlenmiş GUID |
| `InstallerDetector.cs` | MSI / Inno / NSIS / InstallShield tahmini (anahtar adı, komut, dosya başı 512 KB) |
| `FolderSize.cs`, `BroadPaths.cs` | Gerçek boyut ölçümü; geniş kök ve kullanıcı klasörü tespiti (SHGetKnownFolderPath) |
| `FileProbe.cs`, `Authenticode.cs` | Sürüm kaynağı (şirket, ürün) ve imzalayan (WinVerifyTrust, iptal kontrolü yok, yalnız önbellek) |
| `CommandLine.cs`, `ShellLink.cs`, `ScheduledTasks.cs` | Komut satırı ayrıştırma, .lnk ikili biçimi, System32\Tasks XML okuma |
| `NameMatcher.cs`, `Confidence.cs`, `LeftoverModel.cs` | Ad eşleme, kanıt puan tablosu, katman |
| `ScanContext.cs`, `LeftoverScanner.cs` | Anlık görüntü, kalıntı adayları, engellenenler, fark |
| `RestorePoint.cs`, `ProcessTree.cs`, `Uninstaller.cs` | Geri yükleme noktası, iş nesnesiyle süreç ağacı bekleme, üretici kaldırıcısı |
| `RegistryExport.cs`, `SystemActions.cs`, `LeftoverRemover.cs` | .reg yedeği, servis/görev/güvenlik duvarı silme, onaylı kalıntı kaldırma |
| `UninstallJson.cs`, `UninstallHandlers.cs` | Kaynak üreteçli JSON, anlık görüntü deposu, iki worker işleyicisi |

Testler: `tests/DustyBytes.Uninstall.Tests/` (7 dosya). Fikstürler: `tests/fixtures/uninstall/acme.lnk`, `acme-env.lnk`.
Çözüme test projesi eklendi. Core sözleşmelerine dokunulmadı, `DustyBytes.Clean.csproj`'a paket eklenmedi.

## Genel API

- `InstalledPrograms.Enumerate(EnumerateOptions?)` ve `Enumerate(IRegistryView, IFileProbe?, EnumerateOptions?)`.
  Seçenekler: `IncludeMsix`, `MeasureSize`, `DetectBySignature`, `IncludeHidden`.
- `Uninstaller`: `CreateRestorePoint`, `Snapshot`, `RunVendorUninstaller`, `Diff` ayrı ayrı çağrılır, `IProgress<ScanProgress>` bildirir.
  `BuildCommand`: MSI → `msiexec.exe /x {PC} /qb /norestart`; yoksa sessiz dize; yoksa görünür UninstallString.
  `Interpret`: 0, 3010/1641 (yeniden başlatma), 1605, 1602, 1618.
- `LeftoverScanner.Snapshot(program)` → `LeftoverSnapshot { Candidates, Blocked, Notes }`; `Diff(before)`; `GateFolder(path)`.
- `RegistryExport.ToRegFile(reg, keys|items, path)` ve `Render(...)`.
- `LeftoverRemover(reg, scanner, Func<string, Task<bool>> quarantine, ISystemActions?, backupDir?)`.
  `Remove(snapshot, approvedIds)` → `RemovalReport`.
- `UninstallHandlers.HandleUninstall` / `HandleRemoveLeftovers`: `Task<WorkerResponse>(WorkerRequest, IProgress<WorkerProgress>, CancellationToken)`.

## Worker Sözleşmesi

`HandleUninstall`:
- `UserApproved=false` → ret.
- `Target` = program Id (`reg:HKLM64:<anahtar>` ya da `msix:<FullName>`). Program worker içinde yeniden listelenir; arayüzün gönderdiği komut çalıştırılmaz.
- `Items` bayrakları: `skip-restore-point`, `continue-without-restore-point`.
  Geri yükleme noktası başarısızsa ikinci bayrak yoksa uyarıyla durur.
- Akış: nokta → anlık görüntü → üretici kaldırıcısı → nokta tamam/iptal → fark → depo.
- `Payload` = fark anlık görüntüsü JSON (`UninstallJson`).

`HandleRemoveLeftovers`:
- `UserApproved=false` → ret.
- `UnitId` = anlık görüntü Id (32 onaltılık karakter; başka her şey reddedilir). `Items` = onaylı aday Id'leri.
- Yalnız `IsDiff=true` ve program artık kurulu değilse çalışır. Arayüz yol enjekte edemez; yalnız depodaki adaylar arasından seçer.

Worker'ın yapacağı: iki işleyiciyi `Ops.Uninstall` ve `Ops.RemoveLeftovers`'a bağlamak, karantina geri çağrısını (`Func<string, Task<bool>>`) A2'nin karantinasından vermek.

## Kanıt Puan Tablosu

| Kanıt | Puan | Çapa |
|---|---|---|
| Kurulum klasörü (InstallLocation) | +20 | Kurulum klasörü |
| Kurulum klasörü (kaldırıcının konumundan) | +14 | Kurulum klasörü |
| Kurulum klasörünün içinde / ona başvuruyor | +12 | Kurulum klasörü |
| Programın kendi Uninstall anahtarı | +20 | Uninstall anahtarı |
| Sürüm kaynağı şirket / ürün eşleşmesi | +6 / +6 | Sürüm kaynağı |
| İmzalayan eşleşmesi (güvenilir / değil) | +8 / +5 | İmza |
| Yayıncı eşleşmesi | +4 | Yayıncı |
| Ad tam / kısmi | +6 / +3 | Ad |
| Kurulum günüyle aynı oluşturma tarihi | +3 | Kurulum zamanı |
| Şirket uyuşmazlığı | −4 | |
| Başka kurulu programın adı da uyuyor | −6, ad çapası düşer | |
| Store uygulaması | −10 | |

Katman: 2'den az bağımsız çapa → Düşük (gizli). Puan ≥ 16 → Yüksek (işaretli). Puan ≥ 8 → Orta (işaretsiz). Her adayın gerekçe satırı var.

## Asla Kalıntı Olmayanlar

- İndirilenler, Masaüstü, Belgeler (Known Folder yolları), `ProtectedList.NeverLeftoverRoots`: altı da üstü de.
- Korumalı listenin reddettiği yol, geniş kökler (Program Files, AppData, ProgramData, profil, sürücü kökü).
- Taşınabilir uygulama klasörü (appinfo.ini, `portable*` dosyası ya da iki düzeye kadar exe; kurulum klasörü değilse).
- Başka kurulu programın InstallLocation'ıyla çakışan klasör.
- Yayıncı düzeyindeki klasör/anahtar; paylaşılan yayıncıda (Microsoft, Adobe, NVIDIA, Google, Intel, AMD) yalnız tam adlı ürün alt anahtarı.
- Genel ad parçaları (Tools, Update, Common, x64 …) eşleşme sayılmaz.
- Engellenenler `Blocked` listesinde gerekçesiyle durur. Silme anında da yeniden süzülür.

## Silme Kuralları

- Onaylı Id listesi dışında hiçbir şeye dokunulmaz; anlık görüntüde olmayan Id hata satırı olur.
- Dosya/klasör/kısayol → karantina geri çağrısı. Doğrudan dosya silme yok.
- Kayıt işlemlerinden önce `.reg` yedeği `Paths.AppData\registry-backup` altına (UTF-16 LE, BOM). Yedek yazılamazsa kayıt silinmez.
- Servis: SCM `DeleteService`. Görev: XML yedeği + `schtasks /Delete /F`. Güvenlik duvarı: `netsh advfirewall firewall delete rule`.
- `DryRun.Enabled` → yalnız günlük; yedek bile yazılmaz.

## Testler

`dotnet test tests/DustyBytes.Uninstall.Tests`: **98 başarılı, 0 başarısız** (canlı test ortam değişkeni yokken boş geçer).

| Alan | Kapsam |
|---|---|
| Listeleme | 32/64 ve HKCU çift kayıt, farklı ürün aynı anahtar adı, SystemComponent / ParentKeyName / ReleaseType / adsız gizleme, MSI ürün kodu ve UserData konumu, tarih ve boyut |
| Kurucu tahmini | Inno (`_is1`, `unins000`, App Path), MSI, InstallShield, NSIS (ad ve dosya başı) |
| MSI GUID | Office vektörü `{90120000-0030-0000-0000-0000000FF1CE}` ↔ `00002109030000000000000000F01FEC`, bozuk girdiler |
| Eşleme ve puan | Genel ad parçası, ad düzeyleri, yayıncı, paylaşılan yayıncı, iki çapa kuralı, aynı çapa iki kez, başka program cezası |
| Tarayıcı | Kurulum klasörü + Uninstall anahtarı Yüksek; yalnız ad Düşük; başka programın InstallLocation altı düşer; Downloads\Program asla aday değil; NeverLeftover; korumalı liste; taşınabilir; geniş InstallLocation; NVIDIA paylaşılan yayıncı; servis/Run/güvenlik duvarı/ilişki; Store; fark |
| .reg | Başlık, BOM `FF FE`, `@=`, kaçış, hex(1), hex(2), hex(7), dword, hex(b), hex, 80 sütun sarma, WOW6432Node, tek değer |
| Silme ve işleyici | Yalnız onaylı, yedek önce, prova kipi, silme anında kullanıcı klasörü reddi, onaysız ret, bilinmeyen / yol kaçışlı / fark olmayan anlık görüntü reddi, sahte kaldırıcıyla uçtan uca |
| Diğer | .lnk fikstürleri, komut satırı, `\??\` ve `\SystemRoot\`, çıkış kodları, NoRemove, MSIX joker reddi, geniş kökler |

## Ölçümler

Gerçek makinede yalnız `InstalledPrograms.Enumerate()` bir kez çalıştı (`DUSTYBYTES_LIVE=1`):

| Ölçü | Değer |
|---|---|
| Toplam program | 199 |
| MSI | 47 |
| MSIX | 53 |
| NSIS / Inno / InstallShield / bilinmeyen | 28 / 15 / 2 / 54 |
| Boyutu ölçülen | 120 |
| Süre (boyut ölçümü ve dosya başı okuma dahil) | 2,45 sn |

Gerçek kaldırma, geri yükleme noktası, kayıt silme yapılmadı.

## Yapılamayanlar Ve Nedenleri

- **Windows Sandbox testi yapılmadı.** Bu makinede `WindowsSandbox.exe` yok (özellik kapalı). Açmak sistem ayarı değişikliği ve yeniden başlatma ister; bu turun kurallarıyla çelişir.
  Gerçek kaldırma ve geri yükleme noktası bu yüzden yalnız sahte kaldırıcı ve sahte kayıt defteriyle sınandı.
- **Kısayol çelişkisi, kullanıcı kararı:** `protected.json` Başlat Menüsü'nü kök olarak koruyor, NeverLeftover Masaüstü'nü kapsıyor. Bu yüzden programın kısayolları aday olamıyor.
  Kısayollar `Blocked` listesine "kısayol elle silinebilir" notuyla düşüyor. Başlat Menüsü'ndeki `.lnk` dosyalarına istisna açılsın mı, karar sizin.
- **Geri yükleme noktası:** `SRSetRestorePointW` kullanılıyor. Worker süreci `CoInitializeSecurity` çağırmazsa bazı sistemlerde hata dönebilir. WMI `SystemRestore.CreateRestorePoint` yedeği yazılmadı.
  Ayrıca sistem 24 saatte bir noktadan fazlasını sessizce yutabilir.
- **MsiEnumComponents yok:** MSI bileşen yollarından kalıntı çıkarma yapılmadı; MSI için InstallLocation ve UserData yeterli sayıldı.
- **Yalnız geçerli kullanıcı profili taranıyor:** başka kullanıcıların AppData'sı ve HKU kovanları kapsam dışı.
- **Kaldırıcı zaman aşımı yok:** iş nesnesi boşalana kadar beklenir; iptal belirteci beklemeyi bırakır, süreci öldürmez.
