# A1 + A6 Tarama Motoru

Varsayılan tarayıcı (FindFirstFileEx), SQLite dizin, yönetici için MFT hızlı tarama ve USN artımlı güncelleme.
Bağlayıcı belgeler: `docs/dalga/ortak-kurallar.md`, plan bölüm 1, `docs/inceleme/01-disk-cozumleyiciler.md`, `docs/inceleme/02-mft-indeks.md`.

Kanıt dosyaları: ölçüm [`tmp/tarama-olcum.txt`](../../tmp/tarama-olcum.txt), test çıktısı [`tmp/tarama-test.txt`](../../tmp/tarama-test.txt), MFT/FFF fark çözümlemesi [`tmp/tarama-mft-fark.txt`](../../tmp/tarama-mft-fark.txt). Düzeltme öncesi ilk ölçüm (hatalı bulut sayısıyla) [`trash/tarama-olcum-ilk.txt`](../../trash/tarama-olcum-ilk.txt).

## Dosyalar

`src/DustyBytes.Scan/`:

| Dosya | İş |
|---|---|
| `ScanModel.cs` | Sözleşme; yalnız `ScanResult.Usn` (`UsnCursor?`) eklendi |
| `Native.cs` | Elle `LibraryImport`: FindFirstFileEx, GetCompressedFileSize, CreateFile, DeviceIoControl, OpenFileById, GetFinalPathNameByHandle vb. |
| `ScanTree.cs` | `ReparseTags` (ad vekili kuralı, bulut etiketi), `ScanTree.Aggregate` (yinelemesiz son sıra toplama), `ScanTree.Count` |
| `FileScanner.cs` | Varsayılan tarayıcı, akış API'si, hard link dizini |
| `FastScanner.cs` | MFT okuyucu (önyükleme sektörü, runlist, fixup, $ATTRIBUTE_LIST), ağaç kurma |
| `UsnJournal.cs` | FSCTL_QUERY/READ_USN_JOURNAL, `UsnUpdater` artımlı güncelleme |
| `ScanIndex.cs` | `%LOCALAPPDATA%\DustyBytes\index.db`, tek işlemde toplu yazma |

`tests/DustyBytes.Scan.Tests/`: `ScanFixture.cs`, `FileScannerTests.cs`, `IndexAndMftTests.cs`, `BenchTests.cs`. Proje `DustyBytes.slnx`'e eklendi.

## Genel API

```csharp
public sealed class FileScanner : IScanner
{
    public const int BatchSize = 1000;
    public Task<ScanResult> ScanAsync(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel);
    public ScanSession Start(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel);
}
public sealed class ScanSession { ChannelReader<ScanNode[]> Batches; Task<ScanResult> Completion; }

public sealed class FastScanner : IScanner
{
    public const int ChunkSize = 8 << 20;
    public static bool IsSupported(string root);
    public Task<ScanResult> ScanAsync(string root, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancel);
}

public sealed class ScanIndex
{
    public ScanIndex(string? path = null);
    public static string DefaultPath { get; }
    public string Path { get; }
    public long Save(ScanResult result);
    public ScanResult? Load(string root);
    public void SetCursor(string root, UsnCursor cursor);
    public IReadOnlyList<ScanIndexEntry> List();
    public Task<UsnUpdateResult?> RefreshAsync(string root, CancellationToken cancel = default);
}

public sealed record UsnCursor(ulong JournalId, long NextUsn);
public static class UsnJournal
{
    public static string? VolumeOf(string root);
    public static UsnJournalInfo Query(string root);
    public static UsnCursor? TryQueryCursor(string root);
    public static List<UsnChange> ReadSince(string root, UsnCursor cursor, out UsnCursor next, CancellationToken cancel = default);
}
public static class UsnUpdater
{
    public static Task<UsnUpdateResult> ApplyAsync(ScanResult cached, CancellationToken cancel = default);
}
public sealed record UsnUpdateResult(ScanResult Result, int Changes, int Added, int Removed, int Updated, int Unresolved);
public sealed class UsnJournalResetException : Exception;
```

`ReadSince`, günlük kimliği değişmişse, istenen USN silinmişse ya da 1178/1179/1181 hatasında `UsnJournalResetException` atar; çağıran tam taramaya döner.

## Test Çıktısı

`dotnet test tests/DustyBytes.Scan.Tests`: **18/18 geçti** (7,4 s). 13 FileScanner, 3 dizin/MFT/USN, 2 ölçüm (ortam değişkeni yoksa boş geçer).

- FileScanner: iç içe toplam, 260 üstü yol, junction'a girmeme, hard link tek sayım, sıkıştırılmış boyut, erişilemeyen klasör, iptal, Skip ve Quit süzgeci, `Excluded`, küçük tampon, akış (her toplu ≤ 1000, toplam = düğüm sayısı), ilerleme.
- `FastScannerMatchesFileScanner` (`C:\Program Files\dotnet`): dosya, klasör ve mantıksal bayt birebir eşit; ayrılmış bayt oranı 0,95–1,01 arasında.
- `UsnIncrementalUpdate`: geçici klasörde ekle/sil/değiştir, sonra artımlı geçiş: `Değişiklik 14, eklenen 2, silinen 1, güncellenen 2, çözülemeyen 0`.

## Ölçümler

Makine: bu oturumun Windows 11 Pro 22631 sistemi, C: NTFS. Sıra her kök için file → mft → file. Sayılar `tmp/tarama-olcum.txt`'ten aynen alındı.

| Kök | Tarayıcı | Süre ms | Dosya | Klasör | Ayrılmış bayt | Tepe çalışma kümesi | Ağaç belleği |
|---|---|---|---|---|---|---|---|
| C:\Users | file (1.) | 33382 | 2419975 | 765955 | 579059517757 | 1086058496 | 556299968 |
| C:\Users | mft | 8207 | 2361153 | 757027 | 568206110720 | 1042309120 | 544290824 |
| C:\Users | file (2.) | 32420 | 2420059 | 765989 | 579059722557 | 1087016960 | 556317144 |
| C:\ | file (1.) | 33165 | 2896108 | 837602 | 903449321512 | 1344315392 | 663836032 |
| C:\ | mft | 8642 | 2837204 | 828655 | 892475248640 | 1096830976 | 651800656 |
| C:\ | file (2.) | 35416 | 2896139 | 837618 | 903453634600 | 1298804736 | 663825688 |

Dizin (SQLite) ve USN, aynı satırlardan:

| Kök | Tarayıcı | Kaydet ms | Yükle ms | db bayt | USN artımlı ms |
|---|---|---|---|---|---|
| C:\Users | file (1.) | 7219 | 3489 | 217624576 | 388 |
| C:\Users | mft | 6783 | 3568 | 210624512 | 409 |
| C:\ | file (1.) | 8573 | 4304 | 263598080 | 650 |
| C:\ | mft | 8513 | 4189 | 256475136 | 451 |

Soğuk önbellek ölçümü: ilk ölçüm turunda (`trash/tarama-olcum-ilk.txt`) C:\Users için ilk FileScanner 58574 ms, hemen ardından aynı kök 28952 ms. İkinci turda disk önbelleği ısındığı için soğuk koşu yok.

## MFT İle FileScanner Farkı

C:\Users'ta FileScanner'ın gördüğü ama MFT'nin görmediği 8962 klasör ve 58931 dosya var. Nedeni MFT değil: bu oturum Claude masaüstü uygulamasının MSIX paket kabı içinde çalışıyor ve dosya sistemi sanallaştırması `AppData\Roaming` ile `AppData\Local` altına `Packages\Claude_pzs8sxrjxfjjc\LocalCache\...` içeriğini bindiriyor.

Kanıt: `AppData\Roaming\xpdf`'in dosya kimliği `0x000c000000032aac`, `fsutil`'e göre gerçek yeri `\Users\Administrator\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\xpdf`. Farkın grupları: `AppData\Roaming\Claude` 37226, `AppData\Local\Android` 29002, gerisi küçük (tamamı `tmp/tarama-mft-fark.txt`'te). Bu sanal görünümde FileScanner aynı dosyayı iki kez sayar; paketsiz çalışan DustyBytes'ta bu fark beklenmez, ama burada ölçülemedi.

Düzeltilen iki gerçek MFT hatası:

- NTFS'te `0x40000` özniteliği `FILE_ATTRIBUTE_EA`; kayıt defteri kovanları bulut sayılıyordu (ilk turda C:\ için bulut_bayt=41969322943). Artık bulut yalnız bulut reparse etiketiyle; ikinci turda bulut_bayt=0.
- `$MFT`'nin kendi `$ATTRIBUTE_LIST`'i (bu diskte kalıcı olmayan, 128 bayt) okunup başka kayıtlara taşan `$DATA` parçaları runlist'e ekleniyor. Bu diskte `$DATA`'nın 17 parçası kayıt 0'da olduğu için sayı değişmedi; kapsama eksikse hata listesine yazılır.

## Kararlar

- **Hard link:** dizin listelemesi `nNumberOfLinks` vermez. Her dosya için tutamaç açmak milyonlarca çağrı demek. Anahtar (mantıksal boyut, yazma zamanı, oluşturma zamanı) 64 parçalı sözlükte tutulur; yalnız çakışmada iki dosyanın tutamacı açılır (`GetFileInformationByHandle` bağlantı sayısı, `FILE_ID_INFO` kimlik). İlk düğüm tembel çözülür. Tekrar `HardLinkDuplicate` alır, kendi boyutunu korur, toplamaya girmez. MFT tarafında kayıt numarası üzerinden `seen` dizisiyle tek sayım.
- **Reparse:** ad vekili (bit 0x20000000) olan etikete (junction, symlink, bağlama noktası) girilmez, etiket `ReparseTag`'e yazılır. Bulut ve dedup klasörlerine girilir (OneDrive ağacı taranır).
- **Boyut:** sıkıştırılmış/seyrek dosyada `GetCompressedFileSize`, diğerlerinde küme boyutuna yuvarlama. MFT'de kalıcı (resident) dosya 0 ayrılmış bayt sayılır; FileScanner onu bir kümeye yuvarlar. dotnet klasöründe fark 6219386880'e karşı 6224957440 bayt.
- WOF sıkıştırmasında `WofCompressedData` akışı sayılır; diğer adlı akışlar (ADS) sayılmaz.
- FastScanner alt ağaç istense de tüm MFT'yi okur, sonra ada göre kökü bulur.
- USN güncelleyici hard link ayıklamasını yeniden yapmaz; yeni klasörü FileScanner ile yeniden tarar.
- İlerleme ≤ 10 Hz; yüzde yalnız birim kökünde (kullanılan bayta oranla), diğer köklerde −1.

## Yapılamayanlar Ve Sınırlar

- **Erişilemeyen klasör testi ACL ile yapılamadı:** bu ortamda yedekleme ayrıcalığı etkin, FindFirstFileEx deny ACE'yi geçiyor (`C:\System Volume Information` bile listeleniyor). Test, klasörü paylaşımsız tutamaçla kilitleyip `Inaccessible` bayrağını doğruluyor.
- **CsWin32 yerine elle `LibraryImport`:** gereken yapılar (hizalama `Pack = 4` gereken FILETIME'lı yapılar dahil) doğrudan tanımlandı; kural "CsWin32 ya da elle LibraryImport" izin veriyor.
- `tests/fixtures/scan/` kullanılmadı: sahte ağaç her koşuda `Path.GetTempPath()` altında üretiliyor (hard link, junction, sıkıştırma dosya olarak saklanamaz).
- MFT ve FileScanner farkının paketsiz süreçte sıfıra indiği ölçülmedi (yukarıya bakın).
- **Silinemeyen geçici klasörler:** asılı kalan ilk test koşularından kalanlar `%TEMP%` altında duruyor; bir kanca proje dışında silmeyi engelledi: `dustybytes-scan-2878d8a0`, `-74ef9833`, `-787af5cf`, `-d2a2718b`, `-e7b7933f`, `dustybytes-usn-0a4d7e3f`, `-29065bd6`. Bazılarında `denied` altında Everyone için deny ACE var.
