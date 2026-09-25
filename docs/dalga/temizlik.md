# A5 Temizlik — Rapor

Ajan: A5 temizlik kuralları. Klasörler: `src/DustyBytes.Clean/Rules/`,
`src/DustyBytes.Clean/SystemCleanup/`, `rules/cleaners/`, `tests/DustyBytes.Rules.Tests/`,
`tests/fixtures/rules/`.

## Yazılan Dosyalar

**Kural motoru** — `src/DustyBytes.Clean/Rules/`
- `CleanerModel.cs` — `CleanActionType`, `DeleteMode` enum'ları; `CleanAction`, `RunningTest`,
  `CleanerOption`, `CleanerRule`; `CleanerRuleJson` (System.Text.Json kaynak üretici bağlamı)
- `PathGlob.cs` — `ResolveDirectories`/`ResolveFiles`, `*` joker segment genişletme, reparse
  point'leri atlar
- `ICleanupDeleter.cs` — silme arayüzü (gerçek silici başka ajanın modülünde)
- `CleanerResults.cs` — `OptionPreview`, `SkippedPath`, `OptionExecutionResult`, `RuleSelection`
- `RegistryPathResolver.cs` — `HKCU\...`/`HKLM\...` ayrıştırma
- `CleanerCatalog.cs` — `LoadDefault`, `Load`, `Find`, `IsRunning`, `Preview`, `Execute`
- `Winapp2.cs` — `winapp2.ini` ayrıştırıcı (veri gömülmez, yalnız kullanıcı dosyası okunur)

**Kural verisi** — `rules/cleaners/*.json` (23 dosya, en az 15 istenmişti)
windows-temp, user-temp, thumbnail-cache, windows-error-reports, crash-dumps,
directx-shader-cache, chrome, edge, brave, opera, firefox, discord, spotify, teams, slack,
steam, vscode, nvidia-shader-cache, amd-shader-cache, npm-cache, yarn-cache, pip-cache,
nuget-cache. Her dosyada `source` alanı var. Tarayıcı kuralları yalnız
Cache/Cache2/Code Cache/GPUCache/Service Worker CacheStorage'a dokunur — History, Cookies,
Login Data, Web Data, Bookmarks hiçbirinde yok (test ile doğrulandı).

**Sistem temizliği (yönetici)** — `src/DustyBytes.Clean/SystemCleanup/`
- `ProcessRunner.cs`, `ISystemService.cs`, `WindowsSystemService.cs`
- `ISystemCleanupTask.cs` — `SystemCleanupEstimate`, `SystemCleanupResult`
- `FolderSize.cs` — salt okuma boyut ölçümü
- `ComponentStoreCleanup.cs` — DISM `/AnalyzeComponentStore` ve `/StartComponentCleanup`
  (`/ResetBase` sabit metinlerde hiç yok, testle kanıtlı)
- `WindowsUpdateCache.cs` — `wuauserv`/`bits` durdur, `SoftwareDistribution\Download` temizle,
  servisleri önceki duruma döndür
- `DeliveryOptimizationCleanup.cs` — yalnız `Delete-DeliveryOptimizationCache` cmdlet'i
- `DiskCleanup.cs` — `StateFlags1974` + `cleanmgr /sageset:1974` / `/sagerun:1974`,
  "Previous Installations" ve "Temporary Setup Files"
- `WorkerCleanHandlers.cs` — `HandleClean`, `HandleSystemClean` (worker tarafı, `UserApproved`
  olmadan reddeder)

**Testler** — `tests/DustyBytes.Rules.Tests/` (8 dosya) ve `tests/fixtures/rules/`
(`sample-winapp2.ini`, `dism-analyze-sample.txt`)

**Diğer**
- `src/DustyBytes.Clean/DustyBytes.Clean.csproj` — `System.ServiceProcess.ServiceController` eklendi
- `DustyBytes.slnx` — `DustyBytes.Rules.Tests` eklendi
- `docs/licenses.md` — PolarityFlow/CacheFlow (MIT) satırı eklendi; bleachbit ve Winapp2
  satırları zaten vardı (paylaşılan dosya), açıklamaları dosya yollarıyla netleştirildi

## Genel API

```
CleanerCatalog.LoadDefault() / .Load(cleanersDir, protectedList)
CleanerCatalog.IsRunning(CleanerRule rule) : bool
CleanerCatalog.Preview(IReadOnlyList<RuleSelection>) : IReadOnlyList<OptionPreview>
CleanerCatalog.Execute(IReadOnlyList<RuleSelection>, ICleanupDeleter) : IReadOnlyList<OptionExecutionResult>
Winapp2.Parse(string iniText) : IReadOnlyList<CleanerRule>
ISystemCleanupTask.EstimateAsync(ct) / .RunAsync(IProgress<string>, ct)
ComponentStoreCleanup, WindowsUpdateCache, DeliveryOptimizationCleanup, DiskCleanup : ISystemCleanupTask
WorkerCleanHandlers.HandleClean(WorkerRequest, CleanerCatalog, ICleanupDeleter, IProgress<WorkerProgress>?, CancellationToken)
WorkerCleanHandlers.HandleSystemClean(WorkerRequest, IReadOnlyList<ISystemCleanupTask>, IProgress<WorkerProgress>?, CancellationToken)
```

## Test Çıktısı

`dotnet test tests/DustyBytes.Rules.Tests` → **27 başarılı, 0 başarısız** (26 birim/entegrasyon
testi + 1 gerçek makine ölçümü, hepsi tek koşuda).

Kapsam: kural dosyaları geçerli/id'ler tekil, tarayıcı kuralları hassas veriye dokunmuyor,
glob/joker genişletme sahte ağaçta, çalışan-uygulama testi sahte süreç adıyla, winapp2.ini
ayrıştırma, `/ResetBase` hiçbir genel API'den enjekte edilemiyor (yansıma taramasıyla),
DISM analiz çıktısı ayrıştırma sabit metin karşısında, Preview/Execute/koruma listesi/DryRun
davranışları.

## Ölçümler (Gerçek Makine, Salt Okuma)

`RealMachinePreviewTests.PreviewAllRulesOnRealMachineReadOnly` — 23 kuralın tüm seçenekleri
gerçek geliştirme makinesinde `Preview` ile tarandı (hiçbir dosyaya dokunulmadı):

```
TOPLAM: 45000 dosya, 27824849985 bayt (26535,84 MB / ~26,5 GB)
```

DISM tarafında yalnız salt okuma `AnalyzeComponentStore` komutu bir kez gerçek çalıştırıldı
(çıktı ayrıştırma testinde referans olarak kullanılan format bu koşudan doğrulandı).

## Yapılamayanlar ve Neden

- **winapp2.ini indirme yok** — görev tanımı gereği bu dalgada indirme yapılmadı; ayrıştırıcı
  hazır, dosya kullanıcı tarafından `%LOCALAPPDATA%\DustyBytes\winapp2.ini`'ye konduğunda çalışır.
- **DISM `/StartComponentCleanup` gerçek çalıştırılmadı** — güvenlik kısıtı gereği yalnız
  `/AnalyzeComponentStore` (salt okuma) bir kez gerçek çalıştırıldı.
- **Servis durdurma (`wuauserv`/`bits`) ve `cleanmgr` gerçek çalıştırılmadı** — aynı kısıt;
  yalnız testlerde sahte servis/süreç ile doğrulandı.
- **Gerçek silme yapılmadı** — `Execute` yolu `FakeDeleter` ile test edildi; gerçek dosya
  silme başka ajanın (karantina/worker) modülüne devredilir, bu ajan yalnız `ICleanupDeleter`
  arayüzünü çağırır.
