# Danışma 002 girdi: Danışma: DustyBytes Taraması Yavaş, Bulundukça Göstermek

Ajana giden metin:

---

[[danisma:002]]

# Danışma: DustyBytes Taraması Yavaş, Bulundukça Göstermek

## Sahibin Sözü (Aynen)

"ayrıca tarama kısmı halen yavaş işliyor tarama yaptıkça silinebilir özellikleri bastıralım görelim tarzı bir çözüm olabilir bunu da fable a danış"

## Ürün

DustyBytes: Windows disk temizleyici. Avalonia 11 + .NET 10. Yetkisiz arayüz + IPC ile yönetici `--worker`.
Diski tarar, büyük ve az kullanılanı "birim" olarak toplar (oyun, film, program, önbellek, geliştirici artığı,
büyük eski klasör), karantinayla ya da onaylı kalıcı silmeyle yer açar. Bu makine: C:\ ~3,3 M dosya, 1 M klasör,
~4084 birim.

## Bugünkü Hat (Kod Gerçekleri)

- "Taramayı başlat" → `SessionState.RunScanAsync(fast:false)` → `AppBackend.ScanAsync`: arayüz sürecinde
  `FileScanner` (FindFirstFileEx, hard-link tekilleştirme). "Hızlı tarama" (`fast:true`) worker'da `FastScanner`
  (yönetici, MFT'yi doğrudan okur) çalıştırır.
- Sonra zincir tamamen sıralı: tara → `ScanIndex.Save` (SQLite) → `ScanIndex.Load` (yeniden oku, ≤8 bağlantı
  paralel) → `UsageIndex.Collect` → `InstalledPrograms.Enumerate` → `UnitBuilder.Build` (11 çıkarıcı Parallel.For:
  Game, Program, KnownContent, DevArtifact, BrowserCache, Cache, Installer, Series, Film, SystemArtifact,
  LargeOldFolder) → skor.
- Birimler arayüze yalnız zincir bitince tek seferde geçer; arada yalnız yüzde ve metin akar.
- `UsnJournal.cs` + `UsnUpdater.ApplyAsync` artımlı güncelleme yazılı ve testli ama akışa bağlı değil: her tarama tam tarama.
- Çıkarıcıların bir kısmı ağaca bakmadan bilinen yerlerden çalışabilir (Game: Steam/Epic manifestleri; Program:
  kayıt defteri; BrowserCache/Cache: bilinen klasörler), bir kısmı bütün ağacı ister (Film, Series, LargeOldFolder,
  DevArtifact, KnownContent).

## Ölçümler (Bu Makine)

| Aşama | Süre |
|---|---|
| FileScanner tam tarama | 33,2–35,4 sn |
| FastScanner (MFT) tam tarama | 8,2–8,6 sn |
| SQLite kaydet | 7–8,6 sn |
| SQLite yükle | 3,5–4,3 sn (son iyileştirmeyle 1,7 sn) |
| USN artımlı güncelleme | 0,39–0,65 sn |
| Kullanım izleri | ~0,27 sn |
| Kurulu programlar | ~0,19 sn |
| Birim toplama | 1,1 sn |
| Önceki taramayı açma, toplam | 3,3 sn (18,4 sn'den) |

## Kısıtlar

- Worker güvenliği zayıflamaz: her sil/taşı isteği worker'da korumalı listeden geçer.
- Renk ve ölçü yalnız teknesyum-ui token'larından. Arayüz Türkçe.
- Birim kimliği ve boyutu tam taramadakiyle aynı çıkmalı; kısmi sonuç yanlış boyut göstermemeli ya da
  "hesaplanıyor" diye işaretlenmeli.
- Kullanıcı yarım liste üstünde silme yapabilmeli mi, yapmalı mı: sen söyle.

## Soru

1. Taramayı en çok ne hızlandırır, sırasıyla? (Normal taramayı MFT'ye geçirmek, kaydet→yeniden yükle turunu
   kaldırmak, USN artımlıyı bağlamak, SQLite yazımını arka plana almak… ve senin göreceğin başkaları.)
2. "Tarama yaptıkça silinebilir olanları göster" nasıl kurulmalı: hangi birimler ağaç bitmeden kesin
   gösterilebilir, hangileri beklemeli; kısmi boyut nasıl gösterilir; kart sırası akarken zıplamasın diye ne yapılır;
   kullanıcı yarım listede tek tıkla karantinaya alırsa ne olur?
3. Riskler ve ölçüm: hangi sayıyı neyle ölçüp "bitti" diyelim?

Kısa, maddeli, önceliklendirilmiş bir öneri ver; her maddeye hangi dosyanın değişeceğini yaz.
