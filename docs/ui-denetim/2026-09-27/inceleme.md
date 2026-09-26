# Görüntü İncelemesi — İşi Yapmamış Alt Ajan

Alt ajana yalnız görüntüler verildi; kod ve kural dosyası okumadı.

## Gönderilen

Bir Windows masaüstü programının ekran görüntülerine bakıp kullanılabilirlik değerlendirmesi yapacaksın. Kod okuma, kural dosyası okuma; yalnız görüntülere bak (Read aracı PNG'leri gösterir). Hiçbir dosya değiştirme.

Klasör: C:\Users\Administrator\Desktop\Projeler\DustyBytes\docs\ui-denetim\2026-09-27
- `*-100-sonra.png`: gerçek pencere, %100 ölçek, şimdiki hâl (genel, oneriler, harita, programlar, temizlik, karantina, yukleniyor, guncelleme-var, guncelleme-iniyor, guncelleme-hazir, kur-calisiyor, kur-sonuc).
- `*-100-once.png`: aynı ekranların önceki hâli (genel, oneriler, harita, programlar, temizlik, karantina, kur-sonuc).
- `basiz\*-125-sonra.png` ve `basiz\*-150-sonra.png`: aynı programın %125 ve %150 ölçekte başsız görüntüleri; ek durumlar da var (bos-*, hata, bildirimler, onay-tehlikeli, onay-olagan, kaldirma, yukleniyor, guncelleme-*). En az şunlara bak: bos-genel-bakis-150, hata-150, bildirimler-150, onay-tehlikeli-150, onay-olagan-125, kaldirma-150, guncelleme-iniyor-150, oneriler-125, temizlik-150.

Her görüntü için şunu sor: bir kullanıcı ilk bakışta ne yapacağını anlar mı, yazılar rahat okunur mu, bir şey kırpılmış, üst üste binmiş, taşmış, yanlış hizalanmış ya da kaybolmuş mu, düğmeler düğme gibi görünüyor mu, durum (yükleniyor, hata, boş, onay) açıkça anlaşılıyor mu, önce/sonra arasında bozulan bir şey var mı.

Çıktın Türkçe olsun ve yalnız şu biçimde:
1. Tablo: | Görüntü | Gözlem | Önem (engel/orta/küçük) |
2. Sonunda tek satır hüküm: "Kullanılabilir: evet" ya da "Kullanılabilir: hayır — <neden>".
Görmediğin şeyi uydurma; emin değilsen "emin değilim" yaz.

## Dönen (olduğu gibi)

| Görüntü | Gözlem | Önem (engel/orta/küçük) |
|---|---|---|
| genel-100 (önce → sonra) | Kırpılma ve taşma yok. "Prova kipi" uyarısı üst çubuktan sol alt köşeye inmiş, daha az göze çarpıyor ama hâlâ okunuyor. Yol satırları baştan "…" ile kısaltılmış, bu kabul edilebilir. Tarama sırasında "Hızlı tarama" ve "Yeniden tara" gri yazılı; kapalı olduklarından emin değilim. | küçük |
| oneriler-100 (önce → sonra) | Önceki hâlde yollar ortadan "…" ile kısalıyordu, şimdi tam görünüyor; bu bir iyileşme. Seçim yokken "Seçimi temizle" etkin görünüyor. Oyun kartlarında onay kutusu yok, yalnız "Başlatıcıda aç" var. Kullanıcı bunları neden seçemediğini anlamayabilir. | küçük |
| oneriler-125 | Okunaklı, kırpılma yok. Bazı kartlarda onay kutusu var, bazılarında yok (Yeni Oyun). Kutusuz kartta başlık yine aynı sütunda, hizalama tutarlı. | küçük |
| harita-100 (önce → sonra) | Açıkça iyileşmiş. Önceki hâlde "Program Fi…" ve "Program…" etiketleri kırpılıyordu, alt açıklama da dört satıra bölünüyordu. Şimdi etiketler ve açıklama tek satır. Küçük dilimler etiketsiz. Mor çerçeveli küçük dilimin seçili mi, yoksa "Program" türü mü olduğu anlaşılmıyor. | küçük |
| harita-150 | Temiz: renkler, açıklama ve etiketler okunaklı. | — |
| programlar-100 (önce → sonra) | Bozulma yok. Arama kutusunda simge ya da yer tutucu yazı yok, yalnız üstünde etiket var. Kaldırma düğmesi kapalı ve yanında "Önce listeden bir program seçin" ipucu var, bu açık. | küçük |
| temizlik-100 (önce → sonra) | Önceki hâlde kaynak satırı iki satıra bölünüyordu, şimdi tek satır. Tüm boyutlar "Ölçülüyor" derken alt çubuk "yaklaşık 501 MB" diyor; ikisi çelişiyor. Kartlarda son kullanıcıya lisans ve kaynak metni gösteriliyor (bleachbit, GPL, PolarityFlow), bu gürültü. Chrome uyarısında çift parantez var: "(chrome.exe));". | orta |
| temizlik-150 | Okunaklı. Kapalı "Bileşen deposu" onay kutusu etkin kutudan ayırt edilmiyor (gri değil); yalnız alttaki "Bu sürümde kapalı" yazısı söylüyor. O satır da alt çubuğun altında yarım kalıyor (kaydırmayla açılır). | küçük |
| karantina-100 (önce → sonra) | Boş durum açık, "Önerilere git" net bir düğme. Bozulma yok. | — |
| karantina-150 | Uyarı bandı, öğeler ve kapalı "Kalıcı sil" / "Seçilenleri geri al" düğmeleri anlaşılır. "Tümünü seç" düğme gibi görünüyor. | — |
| yukleniyor-100 / -150 | Durum başlığı, yüzde ve çubuk açık. %100'de "x / y kayıt" satırlarının tekrar tekrar alt alta yığılması gürültü yaratıyor. | küçük |
| guncelleme-var-100 / -150 | Güncelleme penceresi yarı saydam: alttaki "Hızlı tarama (yönetici)", "Yeniden tara", "Bekleyen 5,59 GB" ve kart çizgisi içinden sızıyor. Pencere "İptal et" düğmesini ve yüzdeyi örtüyor. "Sonra" ya da kapatma düğmesi yok; nasıl kapanacağı belli değil. | orta |
| guncelleme-iniyor-100 / -150 | Aynı saydamlık: %100'de alttaki "%99 İptal" ilerleme çubuğunun hemen yanında hayalet gibi görünüyor, iki ilerleme birbirine karışabilir. Üst çubuktaki "İniyor %32" rozeti durumu iyi anlatıyor. | orta |
| guncelleme-hazir-100 | Mesaj ve "Yükle" düğmesi net. Saydamlık sorunu burada da var: alttaki yol metni pencere içinden okunuyor. | orta |
| kur-calisiyor-100 | Görüntü kur-sonuc ile birebir aynı (aynı saat, aynı hata). "Çalışıyor" durumu yakalanmamış, değerlendiremedim. | emin değilim |
| kur-sonuc-100 (önce → sonra) | Hata iletisinin can alıcı kısmı (hangi yol) iki yerde de "…" ile kırpılmış; kullanıcı neyin eksik olduğunu göremiyor. Sonra hâlinde "HATA:" satırı sıradan vurgu mavisinde, hata gibi görünmüyor. Önceki hâlde en azından kalın ve belirgindi. Başlığın solunda boşluk var; simge eksik olabilir, emin değilim. | orta |
| bos-genel-bakis-150 | Çok açık: "Henüz tarama yok", ne olacağı ve dolu "Taramayı başlat" düğmesi. "Hızlı tarama (yönetici)" gri yazıyla kapalı gibi duruyor; açık olup olmadığından emin değilim. | küçük |
| bos-oneriler-150 | Boş durum ve "Genel bakışa git" bağlantısı açık. Boş ekranda filtreler ve "Seçimi temizle" etkin görünüyor. | küçük |
| hata-150 | Hata başlığı, nedeni ve "Yeniden dene" net. Kart çerçevesi normal kartlarla aynı mavi, hata metni ise boyut sayılarıyla aynı pembe; hata ayrı bir renkle ayrışmıyor. "Yönetici işçisi" ifadesi kullanıcı için teknik. Bildirim aynı hatayı tekrar ediyor. | küçük |
| bildirimler-150 | Bildirim kartları yarı saydam: alttaki "55,9 GB" ve "5 birim" hata bildiriminin içinden okunuyor. Üstteki bildirim "2,79 GB" satırının yarısını ve listeyi örtüyor. "Önerilere git" düğmesi ile "Kapat" bağlantısı farklı biçimde, ama ikisi de tıklanabilir görünüyor. | orta |
| onay-tehlikeli-150 | Arka plan kararıyor, pencere net. Başlıkta soru işareti yok: "Karantinaya taşınsın mı". Pembe "Taşı" düğmesi olağan pencereden rengiyle ayrılıyor, ama metin tehlikeyi anlatmıyor ("3 gün içinde geri alınabilir"). | küçük |
| onay-olagan-125 | Net. Başlıkta yine soru işareti yok: "Güncelleme yüklensin mi". | küçük |
| kaldirma-150 | Adımlar şimdiki zamanda yazılmış ("oluşturuluyor", "çalışıyor") ama durumları "Bekliyor". İşlem sürüyor gibi okunuyor, kafa karıştırır. Kalıntı listesi, güven etiketleri ve "Programı kaldır" düğmesi açık. | küçük |
| Genel (başsız görüntüler) | "Prova kipi" uyarısı bazı başsız görüntülerde var (bos-genel-bakis, bos-oneriler), bazılarında yok (hata, harita, temizlik-150). Bunun farklı test kurulumundan mı kaynaklandığından emin değilim. | küçük |

Kullanılabilir: evet
