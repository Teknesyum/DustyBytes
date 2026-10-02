# Kullanıcı Verimi Araştırması — 2026-10-02

Konu: ilk açılıştan "yer açıldı" anına kadar geçen süre, kullanıcının verdiği karar ve tıklama sayısı, güven ve
programa geri dönme isteği. Bu kağıt özellik matrisini tekrarlamaz (o, `2026-09-30-piyasa-karsilastirmasi.md`
içinde); akışa ve deneyime bakar. Hiçbir kod değişmedi.

**Kapsam notu (dürüstlük).** Reddit'e bu araçlarla ulaşılamadı: `WebFetch` reddit.com adreslerini reddetti,
`WebSearch` hiçbir Reddit başlığı döndürmedi. Kullanıcı sesi yerine şunlar kullanıldı: GitHub issue ve tartışmaları,
Microsoft Q&A ve Tech Community, Hacker News yorumları (hn.algolia.com üzerinden doğrulandı), Trustpilot ve
SourceForge yorumları, inceleme siteleri. Trustpilot'taki "dosyalarımı sildi" anlatıları kullanıcı iddiasıdır,
doğrulanmış hata değildir; yalnızca korkunun nerede toplandığını göstermek için alındı. Alıntılar kısaltılmıştır.

**Ölçüt notu.** DustyBytes'ta telemetri yok. Aşağıdaki ölçütler bu yüzden ya otomatik testle ya da
sahibin kendi makinesinde (yerel sayaç, dışarı gitmez) ölçülebilir olacak şekilde yazıldı.

## Acı Noktaları

Her satır: kullanıcı nerede vakit kaybediyor, kafası karışıyor ya da korkuyor.

| # | An | Kanıt | Kaynak |
|---|---|---|---|
| 1 | **"Ne yiyor?" sorusunun cevabı yok.** Kullanıcı yer açmayı değil nedenini istiyor; her şeyi temizledi, yine de neyin büyüdüğünü bulamadı. | Microsoft Q&A: "I am interested in knowing what is eating up my storage." Sonunda "hâlâ ilk sıçramanın ne olduğunu bilmiyorum." | https://learn.microsoft.com/en-us/answers/questions/5821271/windows-11-keeps-on-hogging-up-my-disk-space |
| 2 | **Sildi, yer açılmadı (ya da azaldı).** Çöp kutusu, gölge kopya ve "System & reserved" hesaba katılmıyor. | Bir oyunu silip çöpü boşaltan kullanıcıda D: 100 GB'tan 40 GB'a düştü. Başka bir kullanıcıda "System & reserved" 177 GB'a şişti; sebep üçüncü taraf bir anlık görüntü programıydı, analiz araçları göstermedi. | https://techcommunity.microsoft.com/discussions/windowsinsiderprogram/windows-11-d-drive-space-drops-dramatically-after-deleting-game/4510438 , https://learn.microsoft.com/en-us/answers/questions/3164726/deleting-files-does-not-reclaim-disk-space-instead |
| 3 | **Önizlemede görünmeyen şeyin silinmesi.** Güvenin en pahalı kırıldığı an. | BleachBit 4.6.0: "deletes files and folders from ...Desktop although not showing in preview." Hata olarak etiketlendi, 6.0.0'a bırakıldı. Başka bir tartışmada kullanıcı "çok dosyayı yanlışlıkla sildim" diyor. | https://github.com/bleachbit/bleachbit/issues/1702 , https://github.com/bleachbit/bleachbit/discussions/1701 |
| 4 | **Sessiz otomatik silme.** Storage Sense İndirilenler'i "son açılış"a bakarak siliyor; kullanıcı iki yıl sonra fark ediyor. | XDA yazarı kimlik taramaları ve PDF'lerin gittiğini anlatıyor; önerisi: ayarları hatırlatan bir uyarı ve silmeden önce onay. Bir HN yorumcusu: "Windows Storage Sense deleted my personal files." | https://www.xda-developers.com/used-windows-storage-sense-wrong-it-was-deleting-files-i-needed/ , https://news.ycombinator.com/item?id=33117208 |
| 5 | **"Bulutta, silinmedi" anlaşılmıyor.** Disk azalınca dosyalar sessizce yalnız-çevrimiçi oluyor; teknik olmayan kullanıcı kayıp sanıyor. | HN: "Synced files that haven't been used recently will become cloud-only" ile "kandırıldık" tepkisi. Q&A'da kullanıcı: "I am not technical and don't know what I've done"; dosyalar ne bilgisayarda ne bulutta ne çöpte görünüyor. | https://news.ycombinator.com/item?id=40782197 , https://learn.microsoft.com/en-us/answers/questions/5660128/error-reinstalling-onedrive-disk-full-because-of-a |
| 6 | **Temizlik sonrası oturumların kapanması.** Çerez temizliği "çalıştı" ama kullanıcı her sitede yeniden giriş yapıyor. | PC Manager Deep Cleanup bütün kayıtlı sitelerden çıkış yaptırıyor; çözüm "Session" ve "Cookie" kutularını elle kaldırmak. CleanMyMac'te çerez silinince siteler "taking forever" yüklendi. BleachBit'te çerez seçici isteği. | https://learn.microsoft.com/en-us/answers/questions/3916755/pc-manager-logs-me-out-of-many-of-my-saved-website , https://www.trustpilot.com/review/cleanmymac.com?stars=1 , https://github.com/bleachbit/bleachbit/discussions/1850 |
| 7 | **Kopya bulucuda "hangisi kalsın?"** Tüm araçlarda en çok vakit buraya gidiyor. | dupeGuru: referansı "quite arbitrarily" seçiyor, yarı dosya bir klasörde yarı kardeş klasörde kalıyor. Czkawka tartışması: "entirely unclear what 'reference' does", "must tediously select every other item". Czkawka'da "bir klasördeki kopyaların hepsini seç" isteği. | https://github.com/arsenetar/dupeguru/issues/312 , https://github.com/qarmin/czkawka/discussions/1420 , https://github.com/qarmin/czkawka/issues/1324 |
| 8 | **Kopya bulucu varsayılanı tehlikeli ya da sessiz.** | Czkawka CLI: `dups -d` "without saying in the output" dosyayı sildi; issue "tehlikeli" diyor. Czkawka'da çöpe taşıma ayarı varsayılan kapalı; dosyalar kalıcı gidiyor. İnceleme yazarı: "Once files are deleted, they're gone." | https://github.com/qarmin/czkawka/issues/46 , https://www.makeuseof.com/czkawka-open-source-duplicate-file-cleaner-free-up-storage/ |
| 9 | **Kopya sonucu sıfır, sebep yok.** 4 saat tarama, "0 groups"; kullanıcı neyi yanlış yaptığını bilmiyor. | Czkawka issue #1845: arama türü (dosya mı görüntü mü) karışıklığı, hata mesajı yok. | https://github.com/qarmin/czkawka/issues/1845 |
| 10 | **"Silinecek bir şey kalmadı" mesajı hata sanılıyor.** | "We couldn't free up anymore disk space" gören kullanıcı bunun hata olup olmadığını soruyor; cevap: "means there's nothing more to delete." | https://learn.microsoft.com/en-us/answers/questions/2182842/win-11-storage-sense-we-couldnt-free-up-anymore-di |
| 11 | **"Bu klasör nedir, silsem olur mu?"** Analiz araçları boyutu gösterir, anlamını göstermez; uyarı sorumluluğu kullanıcıya bırakılır. | Kullanıcı `C:\Windows\SystemTemp\chrome_BITS_xxxx` için emin değil. XDA, WizTree için "do your research before clearing" uyarısı yapıyor; yanlış silme uygulamaları bozabilir. | https://learn.microsoft.com/en-us/answers/questions/3948289/delete-subfolder-in-c-windowssystemtemp , https://www.xda-developers.com/wiztree-for-drive-analysis/ |
| 12 | **Arka plan süreci, nag, abonelik.** CCleaner'dan kaçışın asıl sebebi. | 7.x'te SmartClean ve Monitoring arka plan süreçleri kapatılamıyor, kapatınca program çalışmıyor; "pop-ups, nags"; Pro 15 $ başlayıp yenilemede 48 $; kaldırınca 120'den fazla gizli kalıntı. PC Manager'ın Bing yönlendirmesi "sisteminiz onarım gerektirir" reklamı olarak eleştirildi. | https://forums.anandtech.com/threads/ccleaner-now-crapifying-itself.2633649/ , https://community.ccleaner.com/t/ccleaner-7-do-not-buy/160220 , https://www.techradar.com/computing/windows/microsoft-stoops-to-new-low-with-ads-in-windows-11-as-pc-manager-tool-suggests-your-system-needs-repairing-if-you-dont-use-bing |
| 13 | **Temizlik sonrası uygulama yavaşladı.** Önbellek silmenin görünmez bedeli. | Steam kullanıcısı CCleaner sonrası açılışın 1 sn'den 10 sn'ye çıktığını bildirdi, Steam'in yapılandırma sıfırlamasıyla düzeldi. XDA: PC Manager Prefetch'i siliyor, kazanç 4,8 MB, risk uygulama yavaşlaması. | https://steamcommunity.com/discussions/forum/1/3198120360740306026/ , https://www.xda-developers.com/things-holding-back-microsoft-pc-manager/ |
| 14 | **İlk açılışta güvensizlik: imza ve antivirüs.** Kullanıcı programı daha çalıştırmadan vazgeçebilir. | BCU'da antivirüs "Trojan:Script/Wacatac" uyarısı ve "kod imzalayın" talebi açık issue. Wise Cleaner Trustpilot'ta "virüs" iddiaları. | https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/issues , https://www.trustpilot.com/review/www.wisecleaner.com?stars=1 |
| 15 | **Tarama eksik ya da saçma boyut.** | WizTree kullanıcısı: harici diskte "skipping some directories", bazı klasörler "ridiculously large sizes". WinDirStat için "always been slow" yorumu. | https://www.snapfiles.com/userreviews/113297/wiztree.html , https://news.ycombinator.com/item?id=49065486 |
| 16 | **Kaldırıcıda paylaşılan bileşen riski ve artık taramasının ayrı olmaması.** | BCU issue'ları: etkin .NET çalışma zamanını kaldırma riski (#1015); kaldırdıktan sonra artık arama yolu yok (#1002). | https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/issues |
| 17 | **Ücretsiz lisansın sürpriz sınırı.** | HN: "WizTree is no longer free for commercial use." TreeSize Free yalnız ticari olmayan kullanım için. | https://news.ycombinator.com/item?id=48940840 , https://knowledgebase.jam-software.com/7054 |

## Rakiplerin İyi Çözdükleri

| Fikir | Kim yapıyor | Neden iyi | Kaynak |
|---|---|---|---|
| **Karo özet + "İncele" düğmesi** | CleanMyMac | Dosya listesi yerine karolar; isteyen "Review" ile tam listeyi, isteyen doğrudan kaldırmayı seçiyor. | https://www.macworld.com/article/352922/cleanmymac-x-review-macos.html |
| **Kabarcık haritası ile sürükle-sil** | CleanMyMac Space Lens | Cihaz yedekleri ve Application Support gibi şişkinlikler gözle bulunuyor. | https://ricksreviews.org/blog/2025/06/12/cleanmymac-space-lens-review/ |
| **Anında tarama** | WizTree, WinDirStat 2.x | WizTree 1 TB SSD'de ~5 sn (WinDirStat eski sürümde 2 dk+). Hız "kullanıcı bekliyor" acısını kapatıyor. | https://www.xda-developers.com/wiztree-for-drive-analysis/ , https://news.ycombinator.com/item?id=46432265 |
| **"En büyük 1000 dosya" ve Explorer gibi sağ tık silme** | WizTree | Öğrenme eşiği yok. | https://www.snapfiles.com/userreviews/113297/wiztree.html |
| **Arama, boyut/tür/sahip süzgeci, CSV ve JSON dışa aktarma, komut satırı, tarama kaydet-yükle, kopya (hash), kurtarma** | WinDirStat 2.x | Değişiklik listesi bunları sayıyor; teknik kullanıcı için tek pencerede iş akışı. | https://github.com/windirstat/windirstat/blob/master/CHANGELOG.md |
| **Tarama geçmişi ve karşılaştırma, rapor (Excel, PDF, HTML), Explorer sağ tık** | TreeSize (ücretli sürüm) | "Ne büyüdü?" sorusunun tek ciddi cevabı; ama Personal $25,20/yıl. | https://www.jam-software.com/treesize/features.shtml |
| **Klasör defteri karşılaştırması (Eklendi / Silindi / Büyüdü / Küçüldü, renkli) ve zamanlanmış başsız taban çizgisi** | altWinDirStat (resmi olmayan çatal) | "Ne değişti"nin en net arayüzü. Resmi WinDirStat'ta yok. | https://github.com/ariccio/altWinDirStat/pull/28 |
| **Kur-unut, varsayılan "disk azalınca"; eşikler ayarlanabilir (İndirilenler gün sayısı, çöp kutusu gün sayısı, bulut boşaltma gün sayısı, sıklık)** | Storage Sense | Programı hatırlamak gerekmiyor. Eksisi: sessiz ve geri alınamaz (Acı 4). | https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense , https://www.tweaktown.com/guides/11411/i-set-up-storage-sense-once-and-havent-worried-about-disk-space-since/index.html |
| **Kategorili onay kutuları, "Bilgisayar Kullanım İzleri" ayrı grup** | PC Manager | Çerez ve oturumu ayrı kutuya almak, kullanıcıya tek tek kapatma imkânı veriyor (ama varsayılanı çıkış yaptırıyor, Acı 6). | https://learn.microsoft.com/en-us/answers/questions/3916755/pc-manager-logs-me-out-of-many-of-my-saved-website |
| **Çerez Yöneticisi (seçili sitelerde oturum kalsın) ve Uzman Modu (deneyimsiz için kapalı)** | BleachBit 6.0 | Güvenlik ile güç arasında kademe. Önizleme-önce önerisi resmî rehberde. | https://ubuntuhandbook.org/index.php/2026/04/bleachbit-6-0-0-cookie-manager-expert-mode/ , https://bleachbit.net/guide/what-are-the-best-practices-for-using-bleachbit-without-risk/ |
| **Akıllı seçim ("en eskiyi/yeniyi koru", yola göre), onay penceresi, sembolik/sabit bağ ile değiştirme** | Czkawka | Hız ve seçim seçenekleri övülüyor; "daha iyi karşılaştırma arayüzü" (dupeGuru'dan geçen kullanıcı). | https://www.makeuseof.com/czkawka-open-source-duplicate-file-cleaner-free-up-storage/ , https://news.ycombinator.com/item?id=36683135 |
| **Bulanık görüntü eşleştirme** | dupeGuru, Czkawka | Fotoğraf kopyaları için; ama yanlış eşleşme var, elle gözden geçirme şart. | https://news.ycombinator.com/item?id=36683135 |
| **Sessiz kaldırma, süzgeç ve derecelendirme, konsolla koşulsuz otomasyon, taşınabilir** | BCU | Toplu iş için; SourceForge'ta 4,8/5 (73 yorum), "yeni başlayana kolay, uzman için bol seçenek". | https://www.bcuninstaller.com/ , https://sourceforge.net/projects/bulk-crap-uninstaller/reviews/ |
| **Hunter modu (simgeye sürükle, kaldır)** | Revo Pro | Program adını bilmeyen kullanıcı için kısa yol. | https://www.revouninstaller.com/online-manual/uninstaller/ |
| **Kazanç tahmini (100 binden fazla topluluk verisi), güncelleme izleyici, geri alma, Explorer sağ tık** | CompactGUI | Sıkıştırmadan önce "bu oyun ne kazandırır" cevabı; oyun güncellenince otomatik yeniden sıkıştırma. Uyarılar: DirectStorage oyunları, zaten sıkıştırılmış oyunlar, HDD'de fayda daha büyük. | https://github.com/IridiumIO/CompactGUI , https://www.xda-developers.com/free-open-source-tool-compress-games-windows-compactgui/ |
| **Hızlı Başlangıç'ı koruyup hazırda bekletme dosyasını küçültme** | Windows (`powercfg /h /type reduced`) | Kapatmak yerine yarıya indirmek; Hızlı Başlangıç gitmiyor. | https://winaero.com/disable-hibernation-but-keep-fast-startup/ |

## Boşluklar

Hiçbir rakibin iyi yapmadığı ama kullanıcıların açıkça istediği ya da yaşadığı şeyler.

1. **"Önceki taramadan beri ne büyüdü?" sıradan kullanıcıya göre.** WizTree'de tarama geçmişi yok; TreeSize'da ücretli; WinDirStat'ta resmi olarak yok, bir çatalda var. Kullanıcı bunu soruyor (Acı 1).
   Kaynak: https://www.foldersizes.com/features/wiztree , https://github.com/ariccio/altWinDirStat/pull/28
2. **Hesaba katılmayan alanın açıklaması.** Sürücü "dolu" ama taranan toplam daha az; fark nedir (gölge kopya, "System & reserved", çöp kutusu, erişilemeyen klasör)? Analiz araçları ya hiç söylemiyor ya da söylese de anlamlandırmıyor (Acı 2, 15).
3. **Silmeden önce "bu nedir, silince ne olur, geri gelir mi" üç satırı.** Araçlar boyutu gösteriyor, sonucu göstermiyor (Acı 11, 13). BleachBit "Uzman Modu"nu ancak yasak koyarak çözüyor.
4. **Önizleme = yürütme garantisi.** Hiçbir araç "az önce gösterdiğim liste tam olarak bu" diye kanıtlamıyor; BleachBit'te tam tersi yaşandı (Acı 3).
5. **Duplicate akışı, teknik olmayan kullanıcı için.** Czkawka ve dupeGuru bile uzmanlara göre; inceleme: basit bir araç arayana uygun değil. Tüm kopyaların işaretlenmesini engelleyen, klasör bazlı seçen, ağaç görünümlü bir akış yok (Acı 7-9).
   Kaynak: https://www.cisdem.com/resource/czkawka-review.html , https://github.com/windirstat/windirstat/issues/656
6. **Tek tıkla "son temizliği geri al" ve "otomatik yaptıklarım" günlüğü.** Storage Sense kendi yaptığını geri getirmiyor, günlüğü yok; Czkawka çöpe taşımayı varsayılan yapmıyor (Acı 4, 8).
7. **Bulut-yalnız işleminin şeffaflığı.** Kullanıcı neyin bulutta olduğunu, çevrimdışı gerekirse ne yapacağını bilmiyor; Storage Sense bunu kendiliğinden yapıyor ama anlatmıyor (Acı 5).
8. **Oturumları varsayılan koruma.** Hâlâ kullanıcıdan "çerezi elle kaldır" bekleyen araçlar var (Acı 6).
9. **Anında yeniden açılış.** Kayıtlı dizinle ilk karede sonuç gösterip arka planda tazeleme; HN'de "offline index" isteği.
   Kaynak: https://news.ycombinator.com/item?id=48938165
10. **Ücretsiz, reklamsız, ticari kullanıma da açık.** WizTree ve TreeSize Free bu sınırı koyuyor (Acı 17); AGPL ürün için sadeleştirme fırsatı.
11. **Hazırda bekletmeyi küçültme önerisi.** Mevcut rehberler "kapat" diyor; "küçült, Hızlı Başlangıç'ı koru" seçeneğini hazır sunan araç görülmedi.

## DustyBytes İçin Sıralı Öneri Listesi

Etki: kullanıcı verimi ve güvenine katkı (1-5). Büyüklük: S gün, M hafta, L birkaç hafta.
Sıra: etki yüksek ve büyüklük küçük olan önce. **Hedef akış ölçütü:** ilk açılıştan ilk onaylı temizliğin
bitişine ≤ 90 sn ve ≤ 3 tık (madde 3'te ölçülür).

| # | Öneri | Etki | Büyüklük | Ölçüt | Dayandığı kaynak |
|---|---|---|---|---|---|
| 1 | **"Ne büyüdü?" ekranı.** Her tarama sonunda küçük bir anlık görüntü sakla (klasör başına boyut, 30 gün); Genel bakışta "son taramadan beri +x GB: şu 3 klasör" kartı; haftalık bildirim cümlesine ekle. Büyüyen klasörün yanına tek tık "İncele". | 5 | M | Kullanıcı testinde "disk neden doldu" sorusuna ≤ 10 sn'de cevap; haftalık bildirimin ≥ %80'inde delta var; kayıt boyutu < 5 MB. | Acı 1; Boşluk 1; TreeSize özellikleri, altWinDirStat, Q&A 5821271 |
| 2 | **Önizleme manifesti = yürütme manifesti.** Kullanıcı "Temizle"ye basınca worker'a gönderilen yol listesi, ekranda gösterilen listenin bayt bayt aynısı olsun; worker listede olmayan yola dokunamasın; bitişte "gösterilen N, silinen N" doğrulaması. | 5 | M | Otomatik test: önizleme yol kümesi ile worker'ın dokunduğu yol kümesi farkı 0; fark varsa temizlik durur. | Acı 3 (BleachBit #1702); Boşluk 4 |
| 3 | **Tek ekran, tek düğme ilk akış.** İlk açılışta tarama biter bitmez karolu özet ("Güvenle açılabilir: 14 GB") ve tek düğme "Güvenli olanı temizle"; altında "Ayrıntıya bak" (CleanMyMac'in Review düğmesi gibi). İmza (A7) bu akışın ön koşulu. | 5 | M | Süre ≤ 90 sn, tık ≤ 3 (açılış → özet → temizle → bitti), sıfır ayar sorusu. Yerel zamanlayıcı ve test senaryosuyla ölçülür. | CleanMyMac karo özeti (Macworld); Acı 14 (imza); piyasa kağıdı #3 |
| 4 | **"Hesaba katılmayan alan" kalemi.** Genel bakışta: sürücü dolu − taranan toplam = "Sistem ayırdığı / gölge kopya / çöp kutusu / erişilemeyen"; her birine tek cümle ve (varsa) tek tık. Gölge kopya için yalnız göster ve Sistem Koruması'na yönlendir. | 4 | M | Sürücü kullanımı ile liste toplamı arasındaki açıklanamayan pay %2'nin altı ya da her kalem adlandırılmış. | Acı 2, 15; Boşluk 2; Q&A 3164726, techcommunity 4510438 |
| 5 | **Dürüst "yer açıldı" sayacı.** Özet iki sayı gösterir: "Şimdi açıldı: x GB (ölçülen)", "7 gün sonra açılacak: y GB (karantinada)". Sayı `DriveInfo` farkından okunur, tahminden değil. Disk %10'un altındaysa "şimdi boşalt" önerisi (piyasa kağıdı #9). | 4 | S | Özetteki "açıldı" ile Explorer'daki boş alan farkı ±%2; "yer açılmadı" şikâyeti test senaryosunda 0. | Acı 2, 10 |
| 6 | **Oturum ve girişi koruma varsayılanı.** Tarayıcı kuralında çerez ve oturum varsayılan işaretsiz; karta "Giriş yaptığınız siteler açık kalır" yaz; isteyen için ayrı "Çerezleri de sil" kutusu ve sonuç uyarısı. | 4 | S | Temizlikten sonra tarayıcı oturumu kaybı: 0 (elle test: Chrome, Edge, Firefox). | Acı 6; PC Manager Q&A 3916755; BleachBit #1850 ve 6.0 Çerez Yöneticisi |
| 7 | **Her kartta üç satırlık "Bu nedir?" dili.** "Bu ne?", "Silersem ne olur?", "Geri gelir mi?" (örnek: "Shader önbelleği: oyun ilk açılışta biraz yavaş açılır, kendiliğinden yeniden oluşur"). Çıktıdaki risk rozeti bu üç satırdan türetilir. | 4 | M | 23 kuralın ve tüm birim türlerinin %100'ünde üç alan dolu (otomatik test); kullanıcı testinde "silince ne olacak" sorusuna kartı okuyarak cevap. | Acı 11, 13; Boşluk 3; Q&A SystemTemp; Steam konusu |
| 8 | **"Son temizliği geri al" ve "Otomatik yaptıklarım".** Oturum başına tek toplu geri alma düğmesi; ayrı bir günlük ekranı: otomatik kuralın neyi, ne zaman karantinaya aldığı, kalan gün; her otomatik kural ayda bir "hâlâ geçerli mi?" diye sorar. Otomatik kural hiçbir zaman kalıcı silmez. | 4 | S | Toplu geri alma ≤ 2 tık; her otomatik eylem günlükte; kuralların kalıcı silme oranı 0. | Acı 4, 8; Boşluk 6; XDA Storage Sense |
| 9 | **Kopya bulucuda akıllı varsayılan.** "Hangisi kalsın" önerisi (en eski yol, en kısa yol ya da seçilen klasör); klasör bazlı "bu klasördekileri seç"; grup başına en az bir kopya kalması kuralı (tümünü seçmek engellenir); ağaç görünümü ("şu klasör tamamen kopya: 8 GB"); sonuç sıfırsa nedeni söyle. | 4 | M | Grup başına tık ≤ 1; "tümü silindi" durumu imkânsız (test); 1000 gruplu sonuçta 10 tık altında onay. | Acı 7-9; dupeGuru #312, Czkawka #1324, #1420, WinDirStat #656 |
| 10 | **Anında açılış.** Son taramanın anlık görüntüsünü ilk karede göster ("2 saat önceki hâl"), USN günlüğüyle arka planda tazele; hazır olunca sessizce güncelle. | 4 | M | İkinci açılışta ilk anlamlı sonuç ≤ 1 sn; tazelenme bitince değişen satır sayısı gösterilir. | Boşluk 9 (HN 48938165); WinDirStat kaydet-yükle; DustyBytes A12 |
| 11 | **Bulut-yalnız akışında güven.** İşlemden önce: "Bu dosyalar bulutta kalacak, çevrimdışıyken açılamaz" onayı ve senkron sağlık denetimi; işlemden sonra: "Silinmedi, bulutta" etiketli liste ve tek tık "Geri indir". | 4 | S | İşlem sonrası kullanıcıdan "dosyalarım nerede" sorusu gerektirmeyen liste; geri indirme ≤ 2 tık. | Acı 5; HN 40782197; Q&A 5660128 |
| 12 | **Bildirim bütçesi.** Haftada en çok bir bildirim, yalnız "açılabilir ≥ 5 GB ya da boş alan %10 altı" iken; "Bu hafta sus" ve "Bir daha gösterme" bildirimin kendisinde; hiç arka plan süreci yok (zamanlanmış görev). | 3 | S | 4 hafta yerel sayaçla: bildirim sayısı ≤ 4; her bildirim bir sayı ve bir düğme içerir; kapatma düğmesi bildirimde. | Acı 12 (CCleaner, PC Manager); Storage Sense varsayılanı |
| 13 | **Arama, süzgeç, klavye.** Ctrl+F ile her ekranda arama; boyut/tür/yaş süzgeci; Enter ile aç, Del ile karantina, Ctrl+Z ile son işlemi geri al; sonuçta çift tıkla Explorer'da aç. | 3 | S | Belirli klasöre ≤ 3 sn'de ulaşma; her ekranda klavye yolu belgeli ve test edilmiş. | WinDirStat 2.x değişiklik listesi; Czkawka #2079, #2036 |
| 14 | **"Küçült" için tahmin ve akıllı dışlama.** Önce tahmini kazanç (örnek sıkıştırmadan), %10'un altındaysa önerme; DirectStorage ve zaten sıkıştırılmış oyunları uyar; oyun güncellenince yeniden sıkıştırmayı öner; geri alma sabit görünür. | 3 | M | Tahmin ile gerçek kazanç farkı ≤ %10; %10 altı kazançlı oyunlarda öneri çıkmaz. | CompactGUI (GitHub, XDA) |
| 15 | **Önce/sonra raporu ve aylık geçmiş.** "Bu ay 12 GB açtınız" geçmişi; tek tıkla HTML ya da CSV rapor (ne büyüdü, ne temizlendi). Paylaşılabilir, telemetrisiz. | 3 | S | Rapor ≤ 2 tık; geçmiş yeniden açılışta görünür; kullanıcı testinde geri dönüş isteği ("yeniden çalıştırır mısınız") yükselir. | Boşluk 1, 6; TreeSize raporları; WinDirStat CSV/JSON; HN sleetdrop |

### Listeye Girmeyen Ama Not Edilen

- **Hazırda bekletme:** "kapat" yerine ikinci seçenek olarak "küçült" (`powercfg /h /type reduced`); Hızlı Başlangıç korunur. Piyasa kağıdındaki #10'a ek. Kaynak: https://winaero.com/disable-hibernation-but-keep-fast-startup/
- **Kaldırıcı:** toplu kaldırma seçiminde .NET ve VC++ gibi paylaşılan çalışma zamanları varsayılan işaretsiz; "kaldırdıktan sonra artık tara" tek başına çalışsın. Kaynak: BCU #1015, #1002.
- **Tarama eksikliği:** taranamayan klasör sayısını ve nedenini göster; harici ve FAT/exFAT sürücüleri ayrıca test et. Kaynak: Snapfiles WizTree yorumu.
- **Erişilebilirlik:** karanlık tema ve yazı ölçeği şikâyet konusu (BCU #961, #1014; Czkawka'da açık tema yok). DustyBytes'ta `UiZoom` var; tema belirteçleri açık ve koyu için ölçüt testine alınmalı.
- **Dışarıda bırakılanlar:** kayıt defteri temizleyici, RAM hızlandırıcı, "sağlık puanı" alarmları bu listede yok; gerekçe piyasa kağıdında.

## Kaynaklar

**Acı noktaları ve övgüler (kullanıcı sesi)**
- Microsoft Q&A, "ne yiyor": https://learn.microsoft.com/en-us/answers/questions/5821271/windows-11-keeps-on-hogging-up-my-disk-space
- Microsoft Q&A, "System & reserved": https://learn.microsoft.com/en-us/answers/questions/3164726/deleting-files-does-not-reclaim-disk-space-instead
- Microsoft Tech Community, oyun silme sonrası alan: https://techcommunity.microsoft.com/discussions/windowsinsiderprogram/windows-11-d-drive-space-drops-dramatically-after-deleting-game/4510438
- Microsoft Q&A, Storage Sense mesajı: https://learn.microsoft.com/en-us/answers/questions/2182842/win-11-storage-sense-we-couldnt-free-up-anymore-di
- Microsoft Q&A, OneDrive kaybı korkusu: https://learn.microsoft.com/en-us/answers/questions/5660128/error-reinstalling-onedrive-disk-full-because-of-a
- Microsoft Q&A, PC Manager oturum kaybı: https://learn.microsoft.com/en-us/answers/questions/3916755/pc-manager-logs-me-out-of-many-of-my-saved-website
- Microsoft Q&A, SystemTemp: https://learn.microsoft.com/en-us/answers/questions/3948289/delete-subfolder-in-c-windowssystemtemp
- Hacker News: https://news.ycombinator.com/item?id=33117208 , https://news.ycombinator.com/item?id=40782197 , https://news.ycombinator.com/item?id=48940840 , https://news.ycombinator.com/item?id=48938165 , https://news.ycombinator.com/item?id=36683135 , https://news.ycombinator.com/item?id=49065486 , https://news.ycombinator.com/item?id=46432265 , https://news.ycombinator.com/item?id=48954816
- BleachBit: https://github.com/bleachbit/bleachbit/issues/1702 , https://github.com/bleachbit/bleachbit/discussions/1701 , https://github.com/bleachbit/bleachbit/discussions/1850 , https://ubuntuhandbook.org/index.php/2026/04/bleachbit-6-0-0-cookie-manager-expert-mode/ , https://bleachbit.net/guide/what-are-the-best-practices-for-using-bleachbit-without-risk/
- Czkawka: https://github.com/qarmin/czkawka/issues/46 , https://github.com/qarmin/czkawka/issues/1324 , https://github.com/qarmin/czkawka/issues/1845 , https://github.com/qarmin/czkawka/discussions/1420 , https://www.makeuseof.com/czkawka-open-source-duplicate-file-cleaner-free-up-storage/ , https://www.cisdem.com/resource/czkawka-review.html
- dupeGuru: https://github.com/arsenetar/dupeguru/issues/312
- WinDirStat: https://github.com/windirstat/windirstat/issues/656 , https://github.com/windirstat/windirstat/blob/master/CHANGELOG.md , https://github.com/ariccio/altWinDirStat/pull/28
- WizTree: https://www.snapfiles.com/userreviews/113297/wiztree.html , https://www.xda-developers.com/wiztree-for-drive-analysis/ , https://www.foldersizes.com/features/wiztree
- TreeSize: https://www.jam-software.com/treesize/features.shtml , https://knowledgebase.jam-software.com/7054
- BCU: https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/issues , https://sourceforge.net/projects/bulk-crap-uninstaller/reviews/ , https://www.bcuninstaller.com/
- Revo: https://www.revouninstaller.com/online-manual/uninstaller/
- CCleaner: https://forums.anandtech.com/threads/ccleaner-now-crapifying-itself.2633649/ , https://community.ccleaner.com/t/ccleaner-7-do-not-buy/160220 , https://steamcommunity.com/discussions/forum/1/3198120360740306026/
- PC Manager: https://www.xda-developers.com/things-holding-back-microsoft-pc-manager/ , https://www.techradar.com/computing/windows/microsoft-stoops-to-new-low-with-ads-in-windows-11-as-pc-manager-tool-suggests-your-system-needs-repairing-if-you-dont-use-bing
- Storage Sense: https://www.xda-developers.com/used-windows-storage-sense-wrong-it-was-deleting-files-i-needed/ , https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense , https://www.tweaktown.com/guides/11411/i-set-up-storage-sense-once-and-havent-worried-about-disk-space-since/index.html
- CleanMyMac: https://www.macworld.com/article/352922/cleanmymac-x-review-macos.html , https://ricksreviews.org/blog/2025/06/12/cleanmymac-space-lens-review/ , https://www.trustpilot.com/review/cleanmymac.com?stars=1
- Wise Cleaner (kullanıcı iddiaları): https://www.trustpilot.com/review/www.wisecleaner.com , https://www.trustpilot.com/review/www.wisecleaner.com?stars=1
- CompactGUI: https://github.com/IridiumIO/CompactGUI , https://www.xda-developers.com/free-open-source-tool-compress-games-windows-compactgui/
- Hazırda bekletme: https://winaero.com/disable-hibernation-but-keep-fast-startup/

**Okunamayanlar.** reddit.com (araç engeli), windowsforum.com ve Trustpilot'un bazı sayfaları (403 ya da yalnız arama özeti),
wiztree.co.uk yönlendirmesi, microsoft/PCManager GitHub deposu (404). SpaceSniffer ve dupeGuru için bu turda
belirgin yeni şikâyet bulunamadı; SpaceSniffer yorumları ağırlıkla olumlu.
