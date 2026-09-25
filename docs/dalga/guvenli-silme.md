# Güvenli Silme Ve Worker — A2 Raporu

Karantina, Geri Dönüşüm Kutusu, doğrudan silme, kilit bilgisi, prova kipi ve yönetici worker.
Sonuç: **44/44 test geçti.** Gerçek sistemde yıkıcı hiçbir şey çalışmadı; testlerin hepsi
`Path.GetTempPath()` altındaki sahte ağaçta koştu.

## Dosyalar

| Klasör | Dosyalar |
|---|---|
| `src/DustyBytes.Core/Protection/` | `ProtectedList.cs` (yalnız ekleme) |
| `src/DustyBytes.Clean/Safety/` | `Native.cs`, `OpResult.cs`, `DryRunLog.cs`, `SafetyGate.cs`, `LockInfo.cs`, `FileTree.cs`, `DirectDelete.cs`, `ShellCom.cs`, `RecycleBin.cs` |
| `src/DustyBytes.Clean/Quarantine/` | `VolumeInfo.cs`, `QuarantineManifest.cs`, `QuarantineStore.cs` |
| `src/DustyBytes.Worker/` | `WorkerNative.cs`, `WorkerHandlers.cs`, `WorkerJson.cs`, `WorkerServices.cs`, `WorkerServer.cs`, `WorkerClient.cs`, `WorkerHost.cs`, `SingleInstance.cs` |
| `tests/DustyBytes.Safety.Tests/` | csproj (çözüme eklendi), `AssemblyInfo.cs`, `TempTree.cs`, `ProtectedListTests.cs`, `RecycleBinTests.cs`, `QuarantineTests.cs`, `DeleteTests.cs`, `WorkerTests.cs` |
| `tests/fixtures/safety/` | Boş. Testler ağacı çalışma anında kuruyor (junction, kilitli dosya, bulut özniteliği), sabit dosyaya gerek çıkmadı. csproj klasörü kopyalamaya hazır. |

`Uninstall/`, `Rules/`, `SystemCleanup/` ve `DustyBytes.Clean.csproj` dokunulmadı.

## ProtectedList Sözleşme Eklemeleri

Mevcut imzalar aynı kaldı; yalnız eklendi:

```csharp
public Func<string, FileAttributes?> AttributeProvider { get; set; } = DefaultAttributes;
public static FileAttributes? DefaultAttributes(string path);
public ProtectedCheck CheckFileSystem(string path, Func<string, FileAttributes?> attributes);
public string? ProtectedNameReason(string name);
```

`CheckFileSystem(path)` artık yeni aşırı yüklemeye `AttributeProvider` ile gidiyor. Bulut yer
tutucu testi sahte öznitelik sağlayıcıyla (`RecallOnDataAccess`, `Offline`) koşuyor.

## Genel API

**Güvenlik kapısı ve sonuç**

- `SafetyGate(ProtectedList)`, `LoadDefault()`, `AddUserDataRoot`, `IsUserData`, `Check(path, includeUserData)`.
  Reddeder: boş ya da göreli yol, `ProtectedList.Check` reddi, `.dustybytes` bölümü, onaysız kullanıcı verisi.
- `OpResult { Path, Status, Message, Method, Bytes, Id, Badge, Holders, Skipped; Ok; IsDryRun }`.
  Durumlar: `Done, DryRun, Denied, Conflict, Locked, NotFound, Failed, Scheduled`.
- `DryRunLog.FilePath` (varsayılan `%LOCALAPPDATA%\DustyBytes\dryrun.log`), `DryRunLog.Write(op, path, detail)`.

**Karantina**

- `QuarantineStore(SafetyGate, QuarantineOptions?, RecycleBin?)`.
- Yöntemler: `Quarantine(path, unitId, includeUserData)`, `Restore(id)`, `Purge(id)`, `PurgeExpired(now?)`,
  `List(includeClosed)`, `Usage()`, `SetExpiry(id, date)`, `KnownRoots`, `DefaultRoot`.
- Kök: `X:\.dustybytes\quarantine`, gizli + sistem, DACL korumalı (Administrators ve SYSTEM tam, kullanıcı okur).
- Öğe adı GUID `N`. Taşıma yalnız aynı birimde, `MoveFileEx` + `MOVEFILE_WRITE_THROUGH`; birim kimliği karşılaştırılır.
- Manifest SQLite `manifest.db`, durumlar `moving → pending → restored | purged`. `PurgeExpired` yarım kalan `moving` satırlarını onarır.
- Geri yükleme: çakışmada üzerine yazmaz, üst klasörü yeniden kurar, zamanları ve öznitelikleri geri koyar.
- `Usage()` birim başına bekleyen bayt; %20 üstü `Warning`. FAT/exFAT, ağ ya da salt okunur birimde Geri Dönüşüm Kutusu'na düşer.

**Geri Dönüşüm Kutusu**

- `RecycleBin.Send(path, includeUserData)`, `List()`, `FindByOriginalPath`, `Restore(item)`, `Purge(item)`.
- `IFileOperation` + `FOF_ALLOWUNDO`, STA iş parçacığında çalışır.
- İlerleme alıcısı `PreDeleteItem` içinde `TSF_DELETE_RECYCLE_IF_POSSIBLE` yoksa işlemi durdurur. Kutu dolu diye kalıcı silmeye kaymaz.
- Özgün konum ve silinme tarihi `SCID_ORIGINAL_LOCATION` ve `SCID_DATE_DELETED` ile okunur.

**Doğrudan silme**

- `DirectDelete(SafetyGate).Delete(path, includeUserData=false, scheduleLockedOnReboot=false)`.
- Reparse noktasını izlemez, bağı siler ama içine girmez. Salt okunur özniteliğini kaldırır, `\\?\` yol kullanır.
- Kilitli dosyayı atlar ve listeler (ilk 16'sının tutucu süreci); `.git` gibi korumalı adları atlar.

**Kilit bilgisi**

- `LockInfo.Holders(path | paths)`, `TryHolders`, `ScheduleDeleteOnReboot(path)`.
- Restart Manager kullanır, süreç öldürmez. Yeniden başlatmaya erteleme `MOVEFILE_DELAY_UNTIL_REBOOT` ile, isteğe bağlı.

**Worker**

- `WorkerHost.Run(args)`: `--worker --pipe <ad> --parent <pid> [--dry-run]`. Açılışta `PurgeExpired` arka planda koşar.
  Çıkış kodları: `0` normal, `64` kullanım hatası, `65` ebeveyn yok, `70` iç hata.
- `WorkerServer(pipeName, parentPid, services, watchParent)`, `RunAsync`, `Stop`, `RejectedClient` olayı.
  Boru ACL'i yalnız çağıran SID, `FirstPipeInstance`. İstemci PID'i `GetNamedPipeClientProcessId` ile `--parent`'a karşı doğrulanır. Ebeveyn ölünce çıkar.
- `WorkerClient.StartAsync(elevated, timeout, ct)`: `runas`; hata 1223 `WorkerStartException.ElevationDenied` olur.
  Ayrıca `ConnectAsync(pipe, expectedServerPid, timeout)` (sunucu PID'ini doğrular), `SendAsync(req, progress?)`, `NewPipeName()`.
- `WorkerHandlers.Register(op, handler)`, `Unregister`, `TryGet`, `Registered`. Yerleşik bir işlemi ezmeye çalışmak `InvalidOperationException` atar.
- `SingleInstance.Acquire(appId)`, `IsFirst`, `Activated` olayı (`string[]`), `StartListening`, `NotifyFirstAsync(args)`.
  Mutex ve etkinleştirme borusu kullanıcı SID'ine bağlı.

## Tel Protokolü

- UTF-8, satır başına bir JSON. İlerleme satırı `p:` + `WorkerProgress`, son satır `r:` + `WorkerResponse`.
- JSON yalnız kaynak üretimli (`WorkerJson`, `IpcJson`).
- Yıkıcı işlemler `UserApproved=true` ister, yoksa ilk adımda reddedilir: `Quarantine, Delete, Purge, Uninstall, RemoveLeftovers, Clean, SystemClean`.
- `Target` kuralları:
  - `Delete` + `on-reboot` kilitlileri yeniden başlatmaya erteler.
  - `Purge` + `expired` süresi dolanları siler.
  - `ListQuarantine` + `all` kapanmışları da listeler.
- `Restore`/`Purge` kimlikleri `Items`'tan, yoksa `Paths`'ten okur. `Payload` sonuç listesinin JSON'udur.
- Prova kipi `--dry-run` argümanıyla geçer, çünkü `runas` ortam değişkenini taşımaz.

## Neden CsWin32 Değil LibraryImport

CsWin32 tek bir paylaşılan `NativeMethods.txt` ister; başka ajanlarla aynı dosyaya yazmamak için
seçildi. COM tarafında `GeneratedComInterface` + `StrategyBasedComWrappers` AOT uyumlu kalıyor.
İkisi de ortak kurallarda izinli.

## Testler

`dotnet test tests/DustyBytes.Safety.Tests` → **Başarılı: 44, Başarısız: 0.** Paralel koşu kapalı,
çünkü prova testi süreç çapında ortam değişkeni kullanıyor. Test başına ad ve süre `tmp/safety-tests.txt` içinde.

| Sınıf | Adet | Kapsam |
|---|---|---|
| ProtectedListTests | 23 | junction, bulut yer tutucu, Common Files / Microsoft Shared, launcher kökü, sürücü kökü, Windows, pagefile/hiberfil/swapfile, NeverLeftover, göreli yol |
| QuarantineTests | 7 | dosya ve klasör gidiş-dönüş, zaman/öznitelik, çakışma, üst klasör, süre dolumu, kilit tutucu, red |
| RecycleBinTests | 2 | gerçek kutu gidiş-dönüşü (yalnız testin kendi dosyası), korumalı yol reddi |
| DeleteTests | 5 | junction hedefine girmeme, kilitli dosya, korumalı ad, LockInfo, prova kipi |
| WorkerTests | 7 | ping, onaysız yıkıcı red, onaylı karantina-liste-geri yükleme, işleyici kaydı, yanlış PID, kapanış, argüman, tek örnek |

En yavaş testler Restart Manager olanlar, 240–300 ms. Kutu gidiş-dönüşü 161 ms.

## Kırmızıdan Yeşile Notu

Koruma testleri silme kodundan önce yazıldı. Bulut yer tutucu ve `SafetyGate` testleri API yokken
derleme kırmızısıydı. Junction, paylaşılan bileşen ve launcher testleri ilk koşuda zaten yeşildi,
çünkü `ProtectedList` hazırdı.

İlk koşular başka ajanın `Clean` derleme hatasına takıldığı için temiz bir kırmızı log yok.

## Yapılamayanlar

- Gerçek UAC/`runas` açılışı ve orta bütünlükten yüksek bütünlüğe boru erişimi denenmedi; UAC penceresi ister.
- `MOVEFILE_DELAY_UNTIL_REBOOT` yalnız prova kipinde test edildi, bilerek.
- exFAT ve ağ sürücüsünde kutuya düşme yolu yalnız kodda var, testte değil.
- AOT yayını denenmedi.
