# Kullanım Sinyalleri — A3 Dalga Raporu

## Yazılan Dosyalar

`src/DustyBytes.Signals/`

| Dosya | İçerik |
|---|---|
| `Vdf.cs` | `Vdf`, `VdfNode` — küçük metin VDF/ACF okuyucu (kendi yazımımız) |
| `Reliability.cs` | `Reliability` sabitleri, `SourceNames` (Türkçe kaynak adları) |
| `Reg.cs` | İç yardımcı: kayıt defteri okuma, yol normalleştirme |
| `SteamLibrary.cs` | `SteamLibrary : IGameLibrary` |
| `EpicLibrary.cs` | `EpicLibrary : IGameLibrary` |
| `GogLibrary.cs` | `GogLibrary : IGameLibrary` |
| `OtherLaunchers.cs` | `PresenceLibrary` (Battle.net, EA), `UbisoftLibrary` |
| `Prefetch.cs` | `PrefetchParser`, `PrefetchInfo`, `PrefetchVolume`, `PrefetchSignal`, `VolumeSerials` |
| `UserAssist.cs` | `UserAssistParser`, `UserAssistEntry`, `UserAssistSignal` |
| `MediaSignals.cs` | `Ini`, `VlcSignal`, `MpcHcSignal` |
| `UsageIndex.cs` | `UsageIndex : IUsageIndex`, `SignalReport` |

`tests/DustyBytes.Signals.Tests/`: `Fixture.cs`, `LibraryTests.cs`, `ExecutableSignalTests.cs`, `MediaAndIndexTests.cs`. Proje `DustyBytes.slnx`'e eklendi.

`tests/fixtures/signals/`: `libraryfolders.vdf`, `libraryfolders_legacy.vdf`, `appmanifest_1091500.acf`, `appmanifest_730.acf`, `epic_game.item`, `vlc-qt-interface.ini`, `mpc-hc64.ini`, `NOTEPAD.EXE-454B5413.pf` (bu makinenin `C:\Windows\Prefetch`'inden salt okunur kopya, MAM sıkıştırmalı).

`SignalModel.cs` sözleşmesine dokunulmadı.

## SteamKit2 Kararı

SteamKit2 3.4.0 paketi `DustyBytes.Signals.csproj`'dan **kaldırıldı**. Gerekçe: `protobuf-net` (yansıma tabanlı, AOT'a uygun değil), `ZstdSharp.Port` ve `System.IO.Hashing` bağımlılıklarını çekiyor; biz yalnız metin VDF okuyoruz. Yerine `Vdf.cs` (~130 satır) yazıldı; SteamKit2 kaynağından kopya yok. Tırnaklı/tırnaksız belirteç, `\\ \" \n \t` kaçışları, `//` yorumları ve `[$WIN32]` koşullarını (atlayarak) destekler.

## Genel API

```csharp
public sealed class SteamLibrary(string? steamRoot = null) : IGameLibrary
    static string? FindSteamRoot();
    IReadOnlyList<string> LibraryFolders();
    static IReadOnlyList<string> ParseLibraryFolders(string vdfText);
    static GameInstall? ParseAppManifest(string acfText, string libraryFolder);
public sealed class EpicLibrary(string? manifestDir = null) : IGameLibrary
    static GameInstall? ParseItem(string json);  static string? FindLauncherDir();
public sealed class GogLibrary : IGameLibrary
public sealed class UbisoftLibrary : IGameLibrary
public sealed class PresenceLibrary : IGameLibrary   // BattleNet(), Ea(): yalnız LibraryRoots

public static class PrefetchParser
    static PrefetchInfo Parse(byte[] raw);  static bool IsCompressed(ReadOnlySpan<byte>);
    static byte[] Decompress(byte[] raw);   static PrefetchInfo ParseDecompressed(byte[]);
    static string? ToDrivePath(string devicePath, IReadOnlyList<PrefetchVolume>, Func<uint,string?> driveForSerial);
public sealed record PrefetchInfo(int Version, string ExeName, IReadOnlyList<DateTimeOffset> LastRuns,
    int RunCount, IReadOnlyList<string> Files, IReadOnlyList<PrefetchVolume> Volumes) { string? ExeDevicePath(); }
public sealed class PrefetchSignal(string? dir = null, Func<uint,string?>? driveForSerial = null) : IExecutableSignal { int Failed; }

public static class UserAssistParser
    static string Rot13(string);  static UserAssistEntry? ParseData(ReadOnlySpan<byte>);
    static string? ResolvePath(string decodedName, Func<Guid,string?> knownFolder);  static string? KnownFolderPath(Guid);
public sealed record UserAssistEntry(int RunCount, int FocusCount, TimeSpan FocusTime, DateTimeOffset? LastRun);
public sealed class UserAssistSignal(Func<Guid,string?>? knownFolder = null) : IExecutableSignal

public sealed class VlcSignal(string? iniPath = null) : IMediaSignal   // static Parse(iniText), SplitQtList, FileUriToPath
public sealed class MpcHcSignal(IReadOnlyList<string>? iniPaths = null, bool useRegistry = true) : IMediaSignal  // static ParseIni, ParseLastOpened

public sealed class UsageIndex : IUsageIndex
    static UsageIndex Collect(IEnumerable<IGameLibrary>? = null, IEnumerable<IExecutableSignal>? = null, IEnumerable<IMediaSignal>? = null);
    UsageSignal ForExecutable(string);  UsageSignal ForFolder(string);  UsageSignal ForMedia(string);
    UsageSignal ForGame(GameInstall);   // ek
    IReadOnlyList<GameInstall> Games;  IReadOnlyList<string> LibraryRoots;  IReadOnlyList<SignalReport> Reports;
    int ExecutableCount;  int MediaCount;
public sealed record SignalReport(string Source, int Count, string? Error);
```

## Davranış Notları

- **Güvenilirlik** (plan tablosu sırası): Steam son oynanma 0.95, Prefetch 0.85, UserAssist 0.75, oynatıcı 0.6, Jump list 0.5, LastWrite 0.3.
- **ForExecutable**: kaynaklar arasında **en yeni tarih** kazanır, eşitlikte güvenilirlik (Prefetch > UserAssist). Görevde "Prefetch > UserAssist" yazıyordu; saf öncelik, Prefetch'te eski bir kayıt varken UserAssist'teki daha yeni çalışmayı yok sayar ve programı olduğundan boşta gösterir. Silme önerisinde bu yanlış yön olduğu için en yeni tarih seçildi, kaynak adı ve güveni o kaydınki.
- **ForFolder**: klasör altındaki exe çalışmalarının en yenisi; `unins`, `setup`, `redist`, `crashhandler`, `installer`, `updater` gibi adlar sayılmaz (kaldırıcıyı çalıştırmak kullanım değil). Klasör bir Steam oyununun içinde ya da üstündeyse Steam `LastPlayed` da aday.
- **Games**: `LastPlayed` yoksa oyun klasörü altındaki Prefetch/UserAssist çalışmalarından doldurulur; bu exe'ler `Executables`'a eklenir. Oyun dizini ayrıca taranmaz.
- **Hata yutma**: her kaynak `try` içinde; hata `Reports`'a `Error` olarak düşer. Prefetch'te okunamayan dosya sayısı `PrefetchSignal.Failed` ve rapor satırında.
- **Prefetch**: MAM başlığı → `RtlGetCompressionWorkSpaceSize` + `RtlDecompressBufferEx` (XPRESS_HUFF=4), elle `LibraryImport`. Sürüm 30/31: ofset 128'den 8 FILETIME, çalışma sayısı EZ kaymasıyla (fi+124, fi+120≠0 ise fi+116). Exe yolu yüklenen dosyalar listesinden ada göre (29+ karakterde kesik ad için önek eşleşmesi); `\VOLUME{...}` öneki birim tablosundaki seri numarasıyla `GetVolumeInformationW` sonucuna eşlenip sürücü harfine çevrilir. Aynı yol için birden çok `.pf` (farklı karma) birleşir: en yeni tarih, sayılar toplanır. Sürüm 17/23/26 desteklenmiyor (Win10/11 hedef).
- **UserAssist**: ≥68 bayt Win7+ yapı (4 sayı, 8 odak sayısı, 12 odak ms, 60 FILETIME), 16 bayt eski yapı (sayı −5, 8 FILETIME). 1970 öncesi null. `{KNOWNFOLDERID}\yol` `SHGetKnownFolderPath` (KF_FLAG_DONT_VERIFY) ile çözülür; yalnız `.exe` ile biten tam yollar sinyal olur.
- **VLC**: `[RecentsMRL] list=` Qt dize listesi (tırnak ve virgül destekli), `file:///` URI çözülür, http vb. atlanır. VLC zaman damgası tutmaz (`times=` oynatma konumu), `LastPlayed` null.
- **MPC-HC**: kayıt `HKCU\Software\MPC-HC\MPC-HC\MediaHistory\<hash>` (`Filename`, `LastOpened` ISO 8601) ve `Recent File List`; aynı biçim `mpc-hc64.ini`/`mpc-hc.ini` içinde (`%APPDATA%\MPC-HC`, `ExePath` klasörü, Program Files). Biçim clsid2/mpc-hc `AppSettings.cpp`'den doğrulandı. İnceleme raporundaki `MPC HC\File Manager Position List` eski sürüm; okunmuyor.
- **Launcher kökleri**: Steam kökü + tüm kütüphaneler; Epic launcher klasörü + oyunların üst klasörleri; GOG Galaxy istemci klasörü + oyun üst klasörleri; Ubisoft launcher + oyun üst klasörleri; Battle.net ve EA yalnız launcher klasörü. Sürücü kökü hiçbir zaman kök olarak eklenmez.
- **Kaldırma**: Steam `steam://uninstall/<appid>`; Epic `com.epicgames.launcher://store/library` (Epic'in herkese açık kaldırma URI'si yok, Playnite de launcher'ı açıyor); GOG kayıttaki `uninstallCommand` ya da `Uninstall\<gameID>_is1` `UninstallString`; Ubisoft `uplay://uninstall/<id>`.

## Test Çıktısı

`dotnet test tests/DustyBytes.Signals.Tests`: **24 test, 24 geçti, 0 kaldı**. `dotnet build src/DustyBytes.Signals`: 0 uyarı, 0 hata (`IsAotCompatible=true` çözümleyicileri açık).

## Gerçek Makine Ölçümü

`UsageIndex.Collect()` bir kez çalıştı (yaklaşık 120 ms):

| Ölçü | Değer |
|---|---|
| Oyun | 7 (Steam 6, Epic 1) |
| Son oynanması bilinen oyun | 4 |
| Exe (tekil yol) | 240 (Prefetch 240, UserAssist 0) |
| Prefetch yolu diskte var | 193 / 240 |
| Medya | 0 (VLC ve MPC-HC kurulu değil) |
| Launcher kökü | 4 |
| Kaynak hatası | 0 |

`C:\Windows\Prefetch`'te 285 `.pf` var; 240 tekil exe yoluna iniyor (aynı exe'nin farklı karmalı dosyaları birleşiyor, birimi eşlenemeyenler atlanıyor). Diskte olmayan 47 yol büyük olasılıkla silinmiş kurulum ve geçici exe'ler.

**UserAssist bu makinede boş**: dokuz `{GUID}\Count` anahtarının hepsinde sıfır değer var (Başlat menüsü program izleme kapalı ya da temizlenmiş). Okuyucu sahte veriyle test edildi; canlı okuma hatasız, sıfır sonuç veriyor.

## Yapılamayanlar

- **Jump list / Recent** sinyali yazılmadı (görev kapsamında değil); `Reliability.JumpList` sabiti yer tutucu.
- **Battle.net ve EA oyun listesi** yok: yalnız "kurulu mu, nerede". Battle.net `Product.db` protobuf, EA kurulum verisi şifreli; görevde istenmedi.
- **Prefetch sürüm 17/23/26** (XP–8.1) desteklenmiyor; `NotSupportedException` fırlatılır, sinyal o dosyayı atlar.
- **MPC-HC `LastOpened` gerçek veriyle doğrulanmadı**: makinede MPC-HC yok; biçim kaynak koddan alındı, fikstürle test edildi.
- VLC ve MPC-HC canlı verisi bu makinede olmadığı için gerçek dosyayla denenmedi.
