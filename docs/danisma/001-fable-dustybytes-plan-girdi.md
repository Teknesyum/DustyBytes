# Danışma 001 girdi: DustyBytes — Plan Görüşü

Ajana giden metin:

---

[[danisma:001]]

# DustyBytes — Plan Görüşü

## Sahibin Cümlesi (Aynen)

"pc mdeki dosyaları tarayıp en büyük ve kullanılması az olanları üstte gösteren bir program tasarımım var fazlalık gereksiz dosyaları silip pcde yer açıcaz format atmak gerekmeyecek artık bu tarz bir program için plan oluştur pp rafımızdakileri kullan sadece dosya değil şu oyun şu film şu klasör vb şeklinde toplu amaç taşıyan dosyalar toplu teklif edilecek şu program kaldırılsın dediğimde kalıntı kalmayacak ccleaner benzeri bir çok programı tara ve istediğimizi yap smooth ve bilgilendirici görsel bir arayüzümüz olmalı"

## Olgular

- Makine: Windows 11 Pro, PowerShell 5.1. Proje klasörü boş, git yok.
- Raf kuralı: web arayüzlü yeni program Tauri 2 + React; medya ve ağır yerel iş Avalonia. Electron yok.
- Kabuk standardı Avalonia için yazılmış (kendi başlık çubuğu, parıltı yalnız kapsayıcıda, tema kitaplığı yasak, testlerle ölçülür).
- Renk ve ölçü yalnız teknesyum-ui token'larından; o depo bu makinede kurulu değil.
- Lisans AGPL-3.0-or-later. README EN + TR. Kur penceresi ve güncelleme rozeti şablondan.
- Raf: geri alınabilir eylem onaylatılmaz, yapılır ve geri alma sunulur. Uzun iş iptal edilebilir, ilerleme tavan kuralıyla.

## Taslak Kararlar

1. Yığın: Avalonia 11 + .NET 9, C#. Gerekçe: MFT okuma, kayıt defteri, WMI, P/Invoke ağır yerel iş.
2. Tarama: NTFS MFT doğrudan okuma (yönetici), yedek yol FindFirstFileEx LARGE_FETCH paralel. Yeniden tarama USN günlüğüyle artımlı. Dizin SQLite önbelleğinde.
3. Kullanım sinyali: LastAccess güvenilmez (NTFS varsayılan kapalı). Onun yerine Prefetch (.pf son 8 çalışma), UserAssist, Amcache, Steam appmanifest LastPlayed, Epic manifest, oynatıcı son dosyalar listesi, LastWrite. Sinyal yoksa "bilinmiyor" gösterilir, uydurulmaz.
4. Birim (toplu teklif): Program, Oyun, Film/Dizi, Fotoğraf albümü, Geliştirici artığı (node_modules, .venv, bin/obj, Docker/WSL vhdx), Önbellek, İndirilen kurulum dosyası, Yinelenen dosya, Klasör.
5. Puan: boyut × boşta kalma süresi × güven. Her teklif "neden" satırı taşır.
6. Silme: doğrudan silme yok. Aynı birime karantina klasörüne taşıma (anlık yeniden adlandırma), süre dolunca ya da kullanıcı onayıyla kalıcı silme. Korumalı yol listesi. WinSxS elle değil DISM ile.
7. Kaldırma: Önce geri yükleme noktası, sonra üreticinin kaldırıcısı (Quiet/MSI /x), ardından kalıntı taraması (kurulum klasörü, AppData, ProgramData, HKCU/HKLM Software, servis, zamanlanmış görev, başlangıç, kısayol, dosya ilişkisi, güvenlik duvarı kuralı). Kalıntılar güven düzeyiyle listelenip karantinaya gider. BCUninstaller (Apache-2.0) incelenir.
8. Süreç: arayüz yönetici değil, yönetici gerektiren iş ayrı bir worker süreçte, named pipe ile.
9. Ekranlar: Genel Bakış, Öneriler, Harita (treemap), Programlar, Temizlik, Karantina.

## Sorular

1. Yığın seçimi doğru mu, yoksa Tauri 2 + Rust çekirdek mi daha iyi?
2. Arayüz/worker ayrımı mı, yoksa bütün program yönetici mi?
3. Karantina modeli tehlikeli bir boşluk bırakıyor mu (disk dolu, farklı birim, açık dosya)?
4. Kalıntı taramasında yanlış pozitif riskini en çok azaltan yöntem hangisi?
5. Aşama sırasında neyi öne almalı, neyi atmalı? En büyük risk nerede?

Cevap Türkçe, madde madde, her madde gerekçesiyle.
