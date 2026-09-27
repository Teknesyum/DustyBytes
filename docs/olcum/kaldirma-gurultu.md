# Kaldırma Ölçümü: Kalan İz, Gürültü Ve Yan Etki

Durum: araçlar yazıldı, henüz çalıştırılmadı. Bu makinede gerçek program kaldırılmadı, gerçek kayıt silinmedi.

## Ne Ölçülür

Bir deneme programı için üç anlık görüntü alınır:

1. **Temel**: program kurulmadan önce.
2. **Kurulu**: kurulup bir kez açılıp kapatıldıktan sonra.
3. **Kaldırıldı**: DustyBytes ile Kaldır → tek onay → bitti akışından sonra.

[diff.ps1](../../tools/uninstall-bench/diff.ps1) her parça için (kayıt, dosya, servis, görev) dört sayı verir:

| Sütun | Anlamı |
|---|---|
| Kurulumla Gelen | Kurulu'da olup Temel'de olmayan satır |
| Kalan İz | Kurulumla gelip Kaldırıldı'da hâlâ duran, gürültü sayılmayan satır: hedef 0 |
| Gürültü | Kalan ama Windows'un kendi yazdığı satır ([noise.txt](../../tools/uninstall-bench/noise.txt)) |
| Yan Etki | Temel'de olup Kaldırıldı'da olmayan satır: başkasına ait bir şey gitmiş; hedef her zaman 0 |

## Nasıl Çalıştırılır

Yönetici PowerShell'de, depo kökünde. Programın adı Programlar listesindeki adıyla birebir yazılır.

```powershell
$d = "tmp\kaldirma\$(Get-Date -Format yyyyMMdd-HHmm)"; powershell -ExecutionPolicy Bypass -File tools\uninstall-bench\snapshot.ps1 -Out "$d\temel"
```

Programı kurup bir kez açıp kapattıktan sonra:

```powershell
powershell -ExecutionPolicy Bypass -File tools\uninstall-bench\snapshot.ps1 -Out "$d\kurulu"
```

DustyBytes ile kaldırmak için ya arayüzden Kaldır'a basılır ya da test kullanılır:

```powershell
$env:DUSTYBYTES_REAL_UNINSTALL = "Program Adı"; $env:DUSTYBYTES_REAL_UNINSTALL_OUT = "$d\kaldirma.txt"; dotnet test tests\DustyBytes.Uninstall.Tests -c Release --filter "FullyQualifiedName~RealMachineUninstallTests" --logger "console;verbosity=detailed"
```

Sonra:

```powershell
powershell -ExecutionPolicy Bypass -File tools\uninstall-bench\snapshot.ps1 -Out "$d\kaldirildi"; powershell -ExecutionPolicy Bypass -File tools\uninstall-bench\diff.ps1 -Base "$d\temel" -Installed "$d\kurulu" -Removed "$d\kaldirildi" -Out "$d\rapor.md"
```

Değişken yoksa test hemen döner. `Kind=Machine` işaretli olduğu için olağan test koşusuna girmez.

## Gürültü Sayılanlar

Programla ilgisi olmadan, Windows'un kullanım sırasında yazdığı yerler. Bunlar kalan iz sayılmaz ama
raporda sayısı görünür:

- Explorer kullanım izleri: RecentDocs, UserAssist, MountPoints2, ComDlg32, FeatureUsage, TypedPaths.
- Klasör görünüm bellekleri: Shell\Bags, BagMRU, MuiCache.
- Uyumluluk yardımcısı: AppCompatFlags\Compatibility Assistant, Layers.
- MSI iç defteri: Installer\Folders ve UserData\...\Components (Windows Installer kendisi temizler).
- Kullanıcının kendi seçimi: FileExts\...\UserChoice ve OpenWithList (Windows korur, biz dokunmayız).
- Defender, sertifika, izleme, arama dizini, bildirim ve teslim iyileştirme kayıtları.
- Servislerin bam/dam girdileri ve Enum alt anahtarları.
- Prefetch, Temp, WER, önbellek ve küçük resim klasörleri, geri dönüşüm kutusu, Config.Msi.
- `System32\Tasks\Microsoft` altındaki Windows görevleri.

Package Cache gürültü sayılmaz: Burn paketinin önbelleği gerçek kalıntıdır.

## Beklenen Sonuç

- Kalan İz 0 ya da yalnız "Orta" güvende bırakılan, kullanıcının işaretlemediği satırlar: dosya ilişkisi
  değerleri, sürücü servisleri, adı programı taşımayan sınıflar.
- "Ayarları koru" işaretliyse AppData altındaki ayar klasörü ve HKCU ayar anahtarı kalan iz olarak
  görünür; bu beklenen davranıştır.
- Yan Etki 0. Sıfır değilse ölçüm başarısızdır ve kaldırma akışı düzeltilmeden yayına çıkılmaz.
