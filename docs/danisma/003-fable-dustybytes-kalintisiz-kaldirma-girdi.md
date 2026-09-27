# Danışma 003 girdi: Danışma: DustyBytes'ta Kalıntısız Kaldırma

Ajana giden metin:

---

[[danisma:003]]

# Danışma: DustyBytes'ta Kalıntısız Kaldırma

## Sahibin Sözü (Aynen)

"artık regedit keyi falan bırakmadan temiz çalışarak sil bu kısmı fable a da danışalım sen de düzgün kaldırmayı araştır"

## Ürün Ve Kısıtlar

DustyBytes: Windows disk temizleyici, Avalonia 11 + .NET 10. Yetkisiz arayüz + IPC ile yönetici `--worker`.
Her sil/taşı isteği worker'da korumalı listeden geçer; bu zayıflamaz. Dosyalar karantinaya gider (7 gün),
kayıt girdileri silinmeden `.reg` yedeği alınır. Sahip "tek tık" istiyor: oyunlar ve büyük içerik zaten tek tıkla
karantinaya gidiyor; kalıcı silme tehlike onayıyla.

## Araştırma (Oku)

Bugünkü kod akışı, açıklar, BCU/Revo/Geek/IObit yöntemleri ve 17 öncelikli öneri tek dosyada:
`C:\Users\Administrator\Desktop\Projeler\DustyBytes\docs\danisma\kaldirma-arastirmasi.md`
Kod: `src/DustyBytes.Clean/Uninstall/*`, arayüz `src/DustyBytes.App/ViewModels/UninstallViewModel.cs`,
`src/DustyBytes.App/Views/UninstallView.axaml`.

Özet: güvenlik sağlam (iki bağımsız kanıt olmadan "Yüksek" yok, .reg yedeği, karantina) ama akış 4 tık
(Kaldır, onay, Kalıntıları sil, onay), hiçbir kalıntı kendiliğinden silinmiyor; sessiz bayraklar kurulum aracına göre
kullanılmıyor; COM/kabuk uzantısı/App Paths/dosya ilişkisi değerleri/StartupApproved/EventLog/MSI yetimleri/SharedDLLs
kapsanmıyor; kurulum klasörü bilinmezse servis/görev/kısayol taramaları atlanıyor; yalnız worker hesabının HKCU'su
taranıyor; kaldırıcıdan sonra ikinci tarama yok; zorla kaldırma yok.

## Soru

1. "Kayıt defteri anahtarı bırakmadan" hedefini güvenli biçimde nasıl tanımlayalım: hangi güven düzeyindeki
   kalıntı onaysız, tek tıkın içinde silinir; hangisi gösterilir; hangisine hiç dokunulmaz (paylaşılan anahtarlar)?
2. Önerilerden hangileri, hangi sırayla? Tık sayısı hedefi ne olmalı (Kaldır → bitti mi)?
3. Sessiz kaldırma: kurulum aracına göre bayraklar, sihirbazı otomatik ilerletmek yerine ne yapılmalı; sessiz
   çalışmayan kaldırıcıda kullanıcıya ne gösterilir?
4. Yükseltme hesabı ≠ oturum hesabı sorunu: HKCU ve AppData doğru kullanıcıdan nasıl okunur?
5. "Bitti" ölçüsü: hangi gerçek programlarla (ör. 7-Zip, VLC, Notepad++, bir MSI, bir MSIX) neyi ölçüp
   "kalıntı sıfır" diyelim; kurulum öncesi/sonrası kayıt ve dosya farkıyla test nasıl kurulur?

Kısa, maddeli, önceliklendirilmiş cevap ver; her maddeye değişecek dosyayı yaz.
