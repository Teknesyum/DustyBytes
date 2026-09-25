# Danışma 001: DustyBytes — Plan Görüşü

claude-fable-5-1, 12.139 çıktı token, 215 sn. Girdi: [001-fable-dustybytes-plan-girdi.md](001-fable-dustybytes-plan-girdi.md). Cevap olduğu gibi:

---

# Danışma 001 Cevap: DustyBytes Plan Görüşü

## 1. Yığın: Avalonia Doğru, Ama .NET 10

- **Avalonia kalsın.** İşin ağırlığı yerel: MFT, USN, kayıt defteri, Restart Manager, Prefetch ayrıştırma, MSI veritabanı. Bunların hepsi CsWin32 kaynak üreteciyle tip güvenli P/Invoke olarak elde var; Rust'ta `windows-rs` ile aynı şey mümkün ama raf standardı (kabuk, kur penceresi, rozet, testler) Avalonia için yazılmış. Tauri seçimi bu şablonları sıfırdan yazdırır.

- **Tek dil, tek DTO.** Arayüz ile worker ayrı süreç olacak (soru 2). İkisi de C# olunca named pipe üzerinden aynı kayıt tiplerini paylaşırlar; Tauri'de Rust çekirdek + TS arayüz iki dil, iki serileştirme katmanı demek.

- **Yükseltilmiş süreçte tarayıcı motoru olmasın.** Tauri'nin WebView2'si yönetici token'ı altında çalışırsa Microsoft'un kendisi bunu önermiyor. Avalonia'da böyle bir motor yok.

- **Treemap Skia'da daha rahat.** 100 bin dikdörtgeni DOM'a basmak kilitler; Tauri'de zaten canvas'a düşersin. Avalonia'da `Control.Render` içinde `DrawingContext` ile tek çizim, hit-test elle. Dikdörtgen başına görsel nesne yaratma.

- **Düzeltme: .NET 9 değil, .NET 10.** .NET 9 STS, desteği Mayıs 2026'da bitti. .NET 10 LTS (Kasım 2028'e kadar). Avalonia 11.2+ ile sorunsuz.

- **Bedel:** Avalonia'da "smooth" animasyon CSS kadar ucuz değil; geçişleri tek bir animasyon yardımcı sınıfına toplayıp oradan yönet. Başlangıç süresi için ReadyToRun + trimming şart, yoksa "yer açan program" 100 MB RAM ile açılır ve marka kendiyle çelişir.

## 2. Arayüz/Worker Ayrımı: Evet, Ve Güven Sınırı Worker'da

- **Bütün program yönetici olursa** Explorer'dan sürükle-bırak çalışmaz (UIPI), açılan her bağlantı ve dosya yönetici olarak açılır, pano tuhaflaşır. Kullanıcı bunu "bozuk" diye okur.

- **Çökme yalıtımı.** 4 milyon dosyalık disk taraması OOM ya da erişim hatası verirse worker düşer, arayüz ve SQLite önbelleği ayakta kalır. Tarama kaldığı yerden devam eder.

- **Yönetici sadece gerektiğinde.** Önbellekten öneri göstermek, treemap gezmek, karantina yönetmek yönetici istemez. UAC yalnız MFT/USN, Prefetch okuma ve kaldırma için, oturumda bir kez.

- **Uygulama:** aynı exe `--worker` argümanıyla çalışsın, ikinci ikili olmasın. Pipe'ı `PipeSecurity` ile yalnız aynı kullanıcı SID'ine aç, ağı reddet; `GetNamedPipeClientProcessId` ile karşı tarafın PID'ini doğrula. Hizmet (service) kurma: kalıcı saldırı yüzeyi, üstelik gereksiz.

- **Asıl kural:** korumalı yol listesi ve her "sil/taşı" denetimi worker'ın içinde çalışsın, arayüzden gelen isteğe güvenmesin. Arayüz yetkisiz, worker yetkili; sınır orada.

## 3. Karantina Modeli: Boşluklar Var, Kapatılabilir

- **Aynı birimde yeniden adlandırma doğru** (anlık, yer istemez). Ama karantina kökü birim başına (`X:\.dustybytes\quarantine\`), gizli+sistem öznitelikli, ACL kısıtlı; içerideki adlar düz GUID, orijinal yol ve ACL/öznitelik/zaman damgaları SQLite manifestinde. Her yerde `\\?\` öneki, yoksa MAX_PATH'te kırılır.

- **"Disk dolu" asıl sorun değil, "yer açılmadı" sorun.** Karantinaya taşınan dosya yer açmaz; kullanıcı yer açmak için geldi. Arayüz iki sayı göstermeli: "bekleyen" ve "kesin". Kategoriye göre politika: önbellek/temp/kurulum indirmesi doğrudan silinir (yeniden üretilir), medya ve klasör karantinaya gider.

- **Açık dosya.** `MoveFileEx` paylaşım ihlali verir. Restart Manager (`RmGetList`) ile tutan süreçleri göster, kapatmayı teklif et; kabul yoksa `MOVEFILE_DELAY_UNTIL_REBOOT` ile yeniden başlatmaya ertele. Süreci sessizce öldürme.

- **Farklı birim: asla kopyala+sil yapma.** Karantina birim başına. Yazılabilir kök yoksa (salt okunur, ağ, FAT32/exFAT ACL'siz) `IFileOperation` + `FOF_ALLOWUNDO` ile Geri Dönüşüm Kutusu'na düş, o da yoksa gerekçesiyle reddet.

- **En tehlikeli boşluk: reparse point ve bulut yer tutucuları.** Junction/symlink'in içinden geçerek silmek hedefi siler; tarama sırasında reparse point'e girme, boyutu dosya kimliğiyle bir kez say. OneDrive "Files On-Demand" yer tutucusu (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`) silince yer açılmaz ama buluttan da silinir. OneDrive/Dropbox/Google Drive kökleri korumalı listeye, üzerinde "bulut, senkron" rozeti.

- **Oyun ve program klasörleri karantinaya elle gitmesin.** Steam kütüphanesinden klasör taşırsan Steam yeniden indirir. Oyun birimi launcher üzerinden kaldırılsın (`steam://uninstall/<appid>`, Epic manifest), program birimi soru 4'teki akıştan geçsin.

- **Kalanlar:** karantina içeriği yeniden taramada dışlanır (yoksa "büyük ve eski" diye geri gelir). Süre dolunca temizlik uygulama açılışında yapılır, hizmet yok. Karantina birimin %20'sini geçince uyarı. `hiberfil/pagefile/swapfile` hiç dokunulmaz, "sistem, ayarlardan" yazılır. `SoftwareDistribution\Download` için servis durdurulur, `Windows.old` için Storage Sense/`cleanmgr` çağrılır. Geri yüklemede orijinal yol doluysa çakışma ekranı, üst klasör yoksa yeniden oluştur.

## 4. Kalıntı Taramasında Yanlış Pozitif: Adla Değil Kimlikle Eşle

- **Tek en iyi yöntem:** kalıntıyı programın kendi kimlik bilgileriyle bul. Ana exe'nin sürüm kaynağındaki `CompanyName`/`ProductName`, Uninstall anahtarındaki `InstallLocation`/`UninstallString`/`Publisher`, ve Authenticode imzalayan adı. Ad benzerliği tek başına asla yetmez; "Tools", "Update", "Client" gibi genel parçalar yasak.

- **MSI ise tahmin yok.** `ProductCode` → `MsiEnumComponents`/`MsiGetComponentPath` kesin dosya listesini verir. MSIX/Store paketi zaten temiz kaldırılır, kalıntı arama gereksiz. Kalıntı sorunu esas olarak NSIS/Inno/özel kurucularda.

- **İki çapa kuralı.** Aday en az iki bağımsız kanıt taşımalı: örn. AppData'da ürün adıyla klasör *ve* içinde kurulum yoluna ya da yayıncıya işaret eden bir dosya; ya da `Software\<Publisher>\<Product>` altında yayıncı adı tam eşleşiyor. Tek çapa = düşük güven, gizli.

- **Paylaşılan bileşen koruması.** Yayıncı anahtarı başka kurulu ürünler de kullanıyorsa (Microsoft, Adobe, NVIDIA, Google) yalnız ürün alt anahtarına dokun. `Common Files`, `Microsoft Shared`, `ProgramData\Package Cache` (başka kurucuların onarım deposu), VC++/.NET/Java runtime'ları beyaz listede.

- **Kaldırıcı sonrası fark.** Üreticinin kaldırıcısını çalıştırmadan önce yüksek güvenli aday kümesinin anlık görüntüsünü al; sonra fark al. Kaldırıcının zaten sildikleri konu dışı, hayatta kalanlar gerçek kalıntı. Bu tek adım yanlış pozitifi en çok düşüren şey.

- **Güven katmanı = varsayılan davranış.** Yüksek (MSI listesi, InstallLocation ağacı, imza eşleşmesi) işaretli gelir; Orta listede işaretsiz; Düşük "daha fazla göster" arkasında. Orta ve Düşük hiçbir zaman otomatik gitmez. Kayıt defteri kalıntısı silinmeden önce `.reg` olarak karantinaya dışa aktarılır; artı geri yükleme noktası.

- **BCUninstaller'dan alınacak fikir:** sertifika/imza eşleştirmesi ve "kurulum tarihine yakın oluşturulmuş" zaman korelasyonu (yalnız destekleyici sinyal). Apache-2.0, AGPL ile uyumlu.

## 5. Sıra, Atılacaklar, En Büyük Risk

- **Aşama 1, salt okunur:** FindFirstFileEx (LARGE_FETCH, dizin bazlı paralel) + SQLite dizin + Genel Bakış + treemap + "büyük ve LastWrite'ı eski" listesi. Silme yok, yönetici yok. Değer ilk gün görünür, risk sıfır, gerçek disklerde performans doğrulanır. SSD'de 1–2 milyon dosya 30–60 sn; MFT'nin 5–10 sn'si şimdilik değmez.

- **Aşama 2:** worker/pipe ayrımı (sonradan eklemek acı verir) + karantina + korumalı liste + en temiz birimler: geliştirici artığı, önbellek, indirilen kurulum dosyası. Bunların sinyali güçlü, riski düşük. **Bundan önce teknesyum-ui deposu makineye kurulmalı**, yoksa renk uydurulur.

- **Aşama 3:** kullanım sinyalleri (Prefetch, UserAssist, Steam/Epic manifest) ve puanlama. Not: `C:\Windows\Prefetch` yönetici ister; UAC'nin ilk gerçek nedeni burası. Puanda ham boyut çarpımı kullanma, 200 GB'lık bir VM imajı her şeyi ezer; log(boyut) ya da sıra tabanlı. "Bilinmiyor" sıfır boşta süresi sayılmaz, ayrı bantta gösterilir.

- **Aşama 4:** kaldırma + kalıntı (soru 4). **Aşama 5:** MFT + USN artımlı tarama "hızlı tarama (yönetici)" olarak.

- **Atılacaklar / ertelenecekler:** Amcache (kilitli hive, VSS ister, Prefetch+UserAssist zaten kapsar: at). Yinelenen dosya (yer kazancı düşük, hash maliyeti yüksek; gelirse boyut kovası → kısmi hash → tam hash, yalnız >10 MB). Fotoğraf albümü (tanımı belirsiz; "≥N görsel içeren klasör" olarak sonraya). Docker/WSL vhdx (silme değil sıkıştırma işi; göster, `wsl --manage --set-sparse` adresine yönlendir, yapma). Tarayıcı önbellekleri (açıkken kilitli; ertele). "CCleaner gibi çok program" için kural dosyası bakım yükü; BleachBit'in CleanerML tanımları GPL-3.0, AGPL-3.0 ile birleştirilebilir, oradan başla; winapp2.ini lisansını ayrıca kontrol et.

- **En büyük risk: geri getirilemeyen bir silme.** Güven ilk hatada biter. Somut dört kapı: bulut yer tutucuları ve senkron klasörler; junction/symlink içinden silme; kalıntı taramasının paylaşılan bileşeni götürmesi; launcher'dan habersiz taşınan oyun klasörü. Dördü de aşama 2'de korumalı liste ve worker denetimiyle kapatılmalı, sonraya bırakılmamalı.

- **İkinci risk: 4 milyon dosyada donan arayüz.** Sonuçlar worker'dan akış halinde gelsin, ilerleme tavan kuralıyla, iptal her an. **Üçüncü:** toplu dosya taşıyan ve kayıt defterine dokunan imzasız bir exe Defender/SmartScreen'e takılır; dağıtım için kod imzası sertifikası bütçeye şimdiden yazılsın.
