# Ortak Kurallar — Dalga Ajanları

Proje kökü: `C:\Users\Administrator\Desktop\Projeler\DustyBytes`. Plan `docs/plan.md`, inceleme raporları `docs/inceleme/`.

## Yığın

- .NET 10, C#, `Directory.Build.props` hedefi `net10.0-windows10.0.19041.0`, `IsAotCompatible=true`, unsafe açık.
- Yansıma tabanlı JSON yok: `System.Text.Json` kaynak üreteci (`JsonSerializerContext`).
- Win32 çağrıları CsWin32 ile (`NativeMethods.txt`), olmazsa elle `LibraryImport`.
- Kabuk yönetici yetkili (elevated). MFT, Prefetch, kayıt defteri HKLM okunabilir.

## Sözleşmeler (değiştirme, yalnız ekle)

- `src/DustyBytes.Core/Model/*`: `Unit`, `UnitKind`, `RemovalMethod`, `UsageSignal`, `Scoring`, `Format`.
- `src/DustyBytes.Core/Protection/*`: `ProtectedList`, `ProtectedRules`, `Verdict`, `Badge`; kurallar `rules/protected.json`.
- `src/DustyBytes.Core/Ipc/Messages.cs`: `WorkerRequest`, `WorkerResponse`, `Ops`, `IpcJson`.
- `src/DustyBytes.Core/Paths.cs`, `DryRun.cs` (`DUSTYBYTES_DRYRUN=1` ise silme ve kaldırma yapılmaz, günlüğe yazılır).
- `src/DustyBytes.Scan/ScanModel.cs`: `ScanNode`, `ScanResult`, `IScanner`, `ScanOptions`.
- `src/DustyBytes.Signals/SignalModel.cs`: `GameInstall`, `IGameLibrary`, `IExecutableSignal`, `IMediaSignal`, `IUsageIndex`.

Bir sözleşmeye alan eklemen gerekirse ekle, var olanı silme ya da yeniden adlandırma; raporda yaz.

## Yazım

- **Kodda yorum yok.**
- **Kod dosyalarını Write/Edit aracıyla yaz.** Bash heredoc bu ortamda `\\` karakterini yutuyor; C# ve JSON'da yol bozulur.
- Kullanıcıya görünen metin Türkçe; tanımlayıcılar İngilizce.
- Yalnız sana verilen klasörlere dokun. Başka ajanlar aynı anda `src/` altında çalışıyor.
- Paket gerekirse yalnız kendi projene `dotnet add package` ile ekle.

## Test

- Kendi test projeni kur: `tests/DustyBytes.<Alan>.Tests` (xunit, `dotnet new xunit`), yalnız ihtiyaç duyduğun projelere referans ver, `DustyBytes.slnx`'e ekle (`dotnet sln DustyBytes.slnx add ...`).
- Fikstürler `tests/fixtures/<alan>/` altında.
- Derleme ve test yalnız kendi projelerinle: `dotnet build src/DustyBytes.X`, `dotnet test tests/DustyBytes.<Alan>.Tests`. Tüm çözümü derleme; başkasının yarım kodu kırar.
- "Dosya kullanımda" türü kilit hatası başka ajanın derlemesidir: birkaç saniye sonra yeniden dene.
- Gerçek sistemde yıkıcı hiçbir şey çalıştırma: gerçek silme, kaldırma, servis durdurma, DISM yok. Testler geçici klasörde (`Path.GetTempPath()` altında) sahte ağaçla çalışır.

## Rapor

İş bitince `docs/dalga/<alan>.md` yaz (Türkçe, başlıklar Title Case): ne yazıldı (dosya listesi), genel API (sınıf ve imzalar), test çıktısı (sayılar), ölçümler, yapılamayanlar ve nedeni. Son mesajında aynı özeti kısa ver.
