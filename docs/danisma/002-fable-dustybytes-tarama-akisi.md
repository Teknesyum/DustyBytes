# Danışma 002: Danışma: DustyBytes Taraması Yavaş, Bulundukça Göstermek

claude-fable-5-1, 12.179 çıktı token, 208 sn. Girdi: [002-fable-dustybytes-tarama-akisi-girdi.md](002-fable-dustybytes-tarama-akisi-girdi.md). Cevap olduğu gibi:

---

# Danışma 002 Cevap: Tarama Hızı ve Bulundukça Gösterme

Kod okundu; süreler girdideki ölçümlerden. Bugünkü bekleme, hesapla: normal tarama ≈ 34 sn tarama + 8 sn SQLite kaydet (Build'den **önce** bekleniyor) + 1,4 sn birim ≈ **44 sn**; hızlı tarama ≈ 8,5 sn MFT + 8 sn worker'da kaydet + 2–4 sn arayüzde yeniden yükle + 1,4 sn ≈ **21 sn**. İkisinde de zamanın yarısı taramada değil, SQLite tur atmasında.

## 1. Hızlandırma, sırasıyla

1. **Kaydetmeyi Build'in arkasına, arka plana al.** `AppBackend.ScanAsync` şu an `ScanIndex.Save` bitmeden `Build` çağırmıyor. `Build`'i önce çalıştır, `Save`'i `_ = Task.Run(...)` ile bırak, snapshot'ı hemen döndür. Normal taramada **−8 sn**, tek dosya, on satır. Kaydetme bitmeden uygulama kapanırsa bir sonraki açılış eski indeksi kullanır; kayıp yok.
   Dosya: `src/DustyBytes.App/Services/AppBackend.cs`.

2. **Hızlı taramada SQLite'ı taşıma formatı olarak kullanmayı bırak.** Worker ağacı SQLite'a yazıyor (8 sn), arayüz aynı ağacı geri okuyor (2–4 sn); sırf süreç sınırını geçmek için 10–12 sn. Ağacı düz ikili biçimde (ön-sıra: ad, boyut, tarih, bayrak, çocuk sayısı) geçici dosyaya yaz; worker yanıtta o yolu döndürsün, SQLite kaydını da yanıttan **sonra** arka planda yapsın. 3,3 M kayıt için yazıp okumak 1 sn'nin altına iner. Hızlı tarama toplamı ≈ **10–11 sn**.
   Dosyalar: `src/DustyBytes.Scan/ScanIndex.cs` yanına `ScanTreeCodec.cs` (yeni), `src/DustyBytes.Worker/WorkerBindings.cs` (`FastScan`), `AppBackend.FastScanAsync`.

3. **USN artımlıyı akışa bağla; "tarama" varsayılanı bu olsun.** Ölçüm zaten var: yükle 1,7 sn + USN 0,4–0,65 sn + birim 1,1 sn ≈ **3,5 sn** ve sonuç bugünkü kadar güncel. Akış: açılışta önbelleği yükle → imleç geçerliyse `UsnUpdater.ApplyAsync` → `Build` → göster. Tam tarama yalnız üç durumda: indeks yok, `UsnJournalResetException`, ya da kullanıcı "Baştan tara" dedi. Kod gerçekleri iki engel koyuyor:
   - `UsnJournal.OpenVolume` sürücüyü `GENERIC_READ` ile açıyor; bu yönetici ister. Ya `Ops.UsnUpdate` diye worker'a taşı (worker zaten ayakta ise UAC yok), ya da `FSCTL_READ_UNPRIVILEGED_USN_JOURNAL` (0x000903AB) kullan: yetkisiz çalışır, ama kullanıcının okuyamadığı dosyaların kayıtlarını atlar. Normal tarama zaten yetkisiz olduğuna göre bu kayıp yok; önce bunu dene, worker yolunu yedek tut.
   - İmleç, tarama **başlamadan önce** alınmalı ki tarama sırasındaki değişiklikler kaçmasın. `FileScanner` 94. satırda bunu yapıyor; `FastScanner` da alıyor. Kontrol et: `ScanIndex.Save` her ikisinin imlecini `scans.usn_journal/usn_next`'e yazıyor mu, yoksa yalnız Fast'inkini mi.
   Dosyalar: `src/DustyBytes.Scan/UsnJournal.cs` (unprivileged FSCTL), `src/DustyBytes.Scan/Native.cs` (sabit), `AppBackend.cs` (yeni `RefreshAsync`), `src/DustyBytes.App/ViewModels/SessionState.cs` (`RunScanAsync` dallanması), `OverviewViewModel.cs` (buton metni: "Yenile" / "Baştan tara").

4. **Normal taramayı MFT'ye geçirme, ama worker ayaktaysa MFT'yi otomatik seç.** 34 → 8,5 sn cazip; bedeli her oturumda UAC ve 001'de kararlaştırılan "yönetici yalnız gerektiğinde" ilkesinden geri adım. Kural basit: `backend.WorkerAlive` ise `fast:true`, değilse `FileScanner`. Kullanıcı bir kez hızlı tarama yaptıysa o oturumda bütün taramalar 8,5 sn. USN akışı bağlanınca bu madde zaten seyrek çalışır.
   Dosya: `SessionState.RunScanAsync`, `IAppBackend` (`WorkerAlive` yoksa ekle).

5. **FileScanner'ın kendisi: ölçmeden dokunma.** `MaxParallelism = ProcessorCount*2`; NTFS meta okumasında SSD'de 32–64 iş parçacığı çoğu zaman daha iyi, ama tek yol denemek. Hard-link tekilleştirmesi zaten yalnız (boyut, yazma, oluşturma) çakışan gruplarda tutamaç açıyor ve `HardLinkIndex.Opened` sayacı var: sayacı ölçüme yaz; 3,3 M dosyada 100 bin üstündeyse eşiği 1 MB'a çek. Bu maddeden 5–10 sn çıkabilir, garanti değil.
   Dosya: `src/DustyBytes.Scan/FileScanner.cs`, `src/DustyBytes.Scan/ScanModel.cs`.

6. **Yapma:** `ScanIndex.Load`'ı daha da hızlandırmak (1,7 sn, artık dar boğaz değil), `UnitBuilder`'ı optimize etmek (1,1 sn), IPC'yi değiştirmek. Sıra: 1 → 2 → 3; 4 ve 5 ölçüm sonrası.

## 2. Bulundukça göstermek

Öncelik tersten: 3. madde bağlanınca gündelik "tarama" 3,5 sn sürer ve bulundukça göstermeye gerek kalmaz. Bulundukça gösterme **yalnız tam tarama** için anlamlı (ilk açılış, journal sıfırlanması). Onu da iki katmanda kur, ağaç bitmeden birim üretmeye kalkma.

- **Katman A, ağaçtan bağımsız birimler (tarama başlar başlamaz, ~1 sn):** Game (Steam/Epic manifestleri), Program (kayıt defteri), BrowserCache/Cache (bilinen klasörler), Installer, SystemArtifact. Bunlar `ctx.Root`'u boyut için kullanıyor; ağaç yokken boyutu `FileScanner.Start` ile o kök klasörleri **öncelikli** taratarak al: tarama kuyruğuna önce manifest/kayıt defterinden gelen klasörleri koy, bittiklerinde o alt ağaç kesindir. Boyut tam, kimlik tam (`UnitIdentity` yol+tür hash'i, ağaca bağlı değil). Kart "kesin" olarak çıkar.
  Dosyalar: `FileScanner.cs` (kuyruğa öncelik + alt ağaç tamamlandı sinyali, `ScanSession.Batches` zaten var), `UnitBuilder.cs` (`Build(ctx, extractors)` aşırı yüklemesi var: A kümesini ayrı çağır), `AppBackend.ScanAsync` (iki aşamalı snapshot).

- **Katman B, bütün ağacı isteyenler (tarama bitince):** Film, Series, KnownContent, DevArtifact, LargeOldFolder. LargeOldFolder "en derin uygun klasör" arıyor; yarım ağaçta yanlış klasörü seçer ve tarama bitince kart kaybolup başka kart doğar. Bunları bekletmek zorunlu. Arayüzde alt bant: "Film, dizi ve büyük klasörler taranıyor · %63".

- **Kısmi boyut gösterme; ya kesin ya "hesaplanıyor".** A katmanında alt ağaç bitmeden kart basılacaksa boyut yerine `—` ve "hesaplanıyor" rozeti, seçim kutusu kapalı. Yanlış boyut göstermek karantina onay metnini ("2,3 GB yer açılır") yalanlar; boş göstermek yalnız sabır ister.
  Dosya: `OffersView.axaml` / `UnitCard` (`OffersViewModel.cs`): `IsSettled` bayrağı, `IsBatch` buna bağlansın.

- **Kartlar zıplamasın:** akış sırasında `Cards`'ı skorla **yeniden sıralama**; yeni kart her zaman **sona** eklenir (`BulkCollection.Add`), tarama bitince tek `ReplaceAll` ile skor sırasına geç ve üstte "Sıralama güncellendi" satırı göster. Kullanıcı yarım listede bir karta baktıysa gözünün altından kaymaz. Alternatif (skor sabit kalıyorsa): kartı skor yerine tür sırasına göre grupla; A katmanı zaten tür tür gelir.
  Dosya: `OffersViewModel.RefreshAsync/Build`.

- **Yarım listede tek tıkla karantina: evet, ama yalnız kesin kartlarda.** Worker zaten her isteği korumalı listeden geçiriyor; birim kesinse (alt ağaç tamam, korumalı kontrolü yapılmış) tarama devam ederken karantinaya almak güvenli. Sonra tarama bitip `Build` yeniden koşunca o yol artık yok; `SessionState.RemoveUnits` ile silinen kimlikleri tut, B katmanı sonucundan düş. Tarama sırasında silinen klasör `FileScanner`'da "yol yok" hatası üretir; bu zaten yutuluyor mu, kontrol et.
  Dosyalar: `OffersViewModel.RemoveAsync`, `SessionState.cs`, `FileScanner.cs` (silinen klasör toleransı).

- **Geçiş süresi hesabı:** Katman A ile ilk kartlar 1–3 sn'de, tamamı 34 sn'de (5. maddeyle daha az). Bu, "tarama yavaş" hissini keser ama toplam süreyi kısaltmaz; asıl kısaltma 1–3. maddelerde.

## 3. Riskler ve "bitti" ölçüsü

- **Riskler:** (a) USN imleci arayüzün göremediği değişiklikleri (yönetici klasörleri) atlar → unprivileged FSCTL kullanılıyorsa bunu yazılı kabul et, normal tarama da aynı sınırı görüyor. (b) İki aşamalı snapshot `Snapshot` tüketicilerini iki kez tetikler (`SnapshotChanged`); Programlar ve Karantina sayfaları buna hazır mı, `ViewModelTests` ile kontrol et. (c) Arka plandaki `Save` ile bir sonraki taramanın `Save`'i çakışırsa SQLite `database is locked`: tek `SemaphoreSlim(1)` yeter. (d) Katman A öncelikli klasörleri kuyruğa alınca `_pending` sayacı ve `Batches` tamamlama sırası bozulabilir; `FileScanner` testleri var, onlara öncelikli kök senaryosu ekle.

- **Ölçüm, bu makinede, üçer koşu, ortalama:**
  - **Açılıştan ilk karta**: hedef ≤ 5 sn (bugün 3,3 + tam tarama beklemesi). USN akışıyla ölçülür.
  - **Normal tarama, başlat → kartlar**: 44 → ≤ 36 sn (1. madde), 5. maddeyle ≤ 30.
  - **Hızlı tarama, başlat → kartlar**: 21 → ≤ 11 sn (2. madde).
  - **USN yenileme sonucu = tam tarama sonucu**: aynı diskte peş peşe; birim kimlik kümesi ve toplam boyut birebir. Farkı olan tek birim varsa bitmemiş sayılır. Bu test `UsnUpdater` testlerinin yanına girer.
  - **Bulundukça gösterme**: ilk kesin kart ≤ 3 sn; kart listesi tarama bitene kadar sıfır yeniden sıralama (test: `Cards` indeks değişimi sayacı = 0).
  Sayılar `docs/olcum/` altında tarih ve commit'le; girdideki tablo genişletilir.

**Tek cümlelik özet:** Önce SQLite'ı bekleme yolundan çıkar (1–2, bir gün), sonra USN'yi varsayılan yap (3, iki gün); bulundukça gösterme ancak tam taramada gerekir ve ağaçtan bağımsız beş tür için kurulur, kalanı bekler.
