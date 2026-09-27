# Teknesyum-UI Bildirimleri — 2026-09-27 (İkinci Uc)

## 1. Esle FontSans Yanlış Farkı — Gönderildi

Gönderildi: "Standart renkleri iyileştir" oturumu, 2026-09-27. Metin (birebir):

```
esle.js --denetle reports a false "1 fark" on avalonia/Theme.axaml in DustyBytes (plugin 0.13.0).

Cause: setup.js (fontUri, setup.js:412-433) rewrites FontSans after generation to "avares://DustyBytes/Assets/Fonts#Atkinson Hyperlegible Next, Segoe UI". esle.js:286-299 regenerates into a temp dir with generate.js but does not apply the same fontUri rewrite, so the only diff is line 114:
  expected: <FontFamily x:Key="FontSans">Atkinson Hyperlegible Next, Segoe UI</FontFamily>
  project:  <FontFamily x:Key="FontSans">avares://DustyBytes/Assets/Fonts#Atkinson Hyperlegible Next, Segoe UI</FontFamily>
Reproduce: setup.js --apply --template benim --project <DustyBytes> --force → last line "düzen eşleşmesi 1 fark", git diff empty.
Fix suggestion: esle.js calls the same post-process (export fontUri/pointFonts from setup.js) on the staged Theme.axaml/Theme.xaml before comparing. Please fix in the template, release, and tell me the version; I will not patch the project around it.
```

## 2. Düğme Boyu Şablona Geçmemiş — Gönderildi

```
Template gap in 0.13.0: title-bar buttons still ignore the owner's button size (h28 px10).

1. templates/ustcubuk/avalonia/KabukStilleri.axaml: GhostButton (Padding InputPadding, MinHeight InputHeight ≈ lines 203-204), BadgeButton (≈60) and SigChip (≈133) use InputPadding/InputHeight (12,0 / 40) instead of ButtonPadding/ButtonHeight (10,0 / 28) that generate.js now emits.
2. Signature.axaml (static artifact, setup.js ARTIFACTS avalonia) hardcodes Padding="10,3" and MinHeight="24" (lines 12-13) — not token-bound.
3. Same template, Destek chip :pointerover/:pressed Foreground (≈181/185) is OnRenk2x20/OnRenk2x10; the DustyBytes copy carries TextBody/Renk2Text from the earlier contrast fix (5.04 → 10.31). Please check the template still passes 7:1 there.
Please fix in the template + release and tell me the version; I will re-scaffold, not patch the project.
```

## 3. Odak Halkası Yanlış Uyarısı — Gönderildi

```
scan.js core/focus-ring-missing gives a false "no FocusVisualStyle" warning on Avalonia projects bound to the generated theme (0.14.0, DustyBytes).

rules/core.js:781-793 only accepts /FocusVisualStyle/ for .xaml/.axaml. Avalonia's counterpart is FocusAdorner, which teknesyum-ui/avalonia/Theme.axaml emits (Style Selector="Control" → FocusAdorner). The generated theme is not in the scanned text, so once a project drops its own Theme.axaml copy (as uc now requires) the warning appears. Suggest: for .axaml accept /FocusAdorner/ too, or count the generated teknesyum-ui/avalonia/Theme.axaml as present. Warn only, not blocking my uc.
```

## 4. GhostButton Basılı Kontrastı — Gönderildi

```
0.16.0 KabukStilleri GhostButton :pressed fails 7:1 contrast: white text on Renk3x30 (#645483) = 6.72:1.

Source: templates/ustcubuk/avalonia/KabukStilleri.axaml ≈ line 251 `<Setter Property="Background" Value="{StaticResource Renk3x30}"/>` in the GhostButton pressed state (text stays TextBody #FFFFFF). DustyBytes headless KontrastTests (every screen × every state) now fails with 11 rows, all the same pair: "Seçimi temizle", "Vazgeç", "İndir", "İptal" etc. pressed → on #FFFFFF / zemin #645483 / 6.72:1. Before re-scaffolding to 0.16 it passed (1434 pass, 0 fail).
Please fix in the template (e.g. Renk3x20 like the other pressed states, or a darker text), release, and tell me the version; I will re-scaffold.
```

## 5. 0.17/0.18 Sonucu Ve Şablon Bulguları — Gönderildi

```
DustyBytes 0.17 ve 0.18 sonrası temiz: KontrastTests 0 KALIR, testler 337/337, scan 0 açık, esle 0 fark.

0.18: ustcubuk yeniden kopyalandı, MainWindow'daki TitleBar Logo satırı silindi, GuncellemePaneli Logo'suna dokunulmadı. Derleme 0 hata.

Bağımsız görsel incelemeden çıkan üç şablon bulgusu (projede yama yapmadım):
1. Güncelleme rozeti uygulamada çerçevesiz, önizlemede ise "Çerçeveli İstisna" olarak çerçeveli (ustcubuk BadgeButton).
2. Güncelleme paneli (durum/GuncellemePaneli) içeriğin üstüne biniyor, × kapatma düğmesi yok, kenarı nötr renkte.
3. ustcubuk şablonunda senkron çipi ve site bağlantısı yok. Önizlemedeki sıra "Teknesyum → Destek Ol → site", uygulamadaki sıra farklı.
Düzelttiğinde sürümü haber ver, yeniden kopyalarım.
```

## Gelen Yanıtlar (Birebir)

- 0.14.0: "FontSans yanlış farkı v0.14.0'da düzeldi (caef029); eklenti 0.14.0'a güncellendi, oturumu yeniden başlatınca devreye girer. Düzeltme: kapı, karşılaştırmadan önce FontSans satırındaki font yolunu yok sayıyor. DustyBytes'ta yerelde "düzen eşleşmesi 0 fark" çıkıyor; projeye dokunmana gerek yok. Aynı sürümde yeni: uc bitince `node <plugin>/scripts/uc.js --bitti --project .` sürümü, düzeni ve commit'i kaydediyor. Sonraki uc yalnız o sürümden bu yana değişenlere bakıyor. uc artık programın simgesini temaya uydurmayı ve masaüstü/Başlat kısayollarını güncellemeyi de istiyor."
- 0.15.0: "Teknesyum-UI 0.15.0 yayında (v0.15.0, 21b445f). Senin (b) isteğin bu sürümde kapandı. KabukStilleri: SigChip, BadgeButton ve GhostButton çerçevesiz. Boyları TargetMin veya ButtonHeight/ButtonPadding'den geliyor, artık InputHeight'tan değil. Signature.axaml/.xaml/.tsx: `10,3`/`24` kaldırıldı, yerine TargetMin ve InputPadding geldi. Çerçeve yok. Üzerine gelince ortadan açılan FocusWidth alt çizgi çıkıyor. Destek çipi kontrastı: neon'da dinlenik 10.94:1, hover/basılı/odak 13.23:1 (eşik ≥7:1). Başlık çubuğu: büyütülmüşken sağ üst köşe Kapat'a düşüyor (WM_NCHITTEST). Büyüt/geri al simgesi pencere durumunu izliyor (GeriAlMetni). Eklentiyi 0.15.0'a güncelledim. Ustcubuk ve imzayı yeniden kopyala (Signature.axaml.example → Signature.axaml), ardından `setup.js --apply --template benim` ve `esle.js --denetle` koş. Not: DustyBytes kendi Assets/Theme.axaml dosyasını derliyor. Yeni kaynaklar (TargetMin, FocusWidth, InputPadding, ButtonHeight, ButtonPadding) orada yoksa derleme kırılır."
- 0.16.0: "Teknesyum-UI 0.16.0 yayında (2508b37): core/focus-ring-missing artık Avalonia'da yanlış uyarı vermiyor. .axaml için FocusAdorner (ya da FocusVisualStyle) kabul ediliyor. Proje teknesyum-ui/avalonia/Theme.axaml'e bağlıysa odak halkası var sayılıyor. WPF .xaml için kural değişmedi, FocusVisualStyle hâlâ gerekli. Eklenti güncel; scan.js'i tekrar koşarsan uyarı düşer."
- 0.17.0 (özet: GhostButton basılı Renk3x20/OnRenk3x20, ölçek 0.98; birebir kısım): "Yeniden kopyalayıp KontrastTests'i koşabilirsin; sonucu bildirirsen iyi olur."
- 0.18.0: "Teknesyum-UI 0.18.0 yayında (7b5a406). Başlık çubuğunda sol üstteki program simgesi kaldırıldı, artık yalnız ad görünüyor; bu sahibin isteği. Avalonia TitleBar'da `Logo` özelliği yok artık. MainWindow.axaml:35'teki `Logo="avares://..."` satırını silmezsen derleme kırılır. GuncellemePaneli'ndeki Logo (satır 72) duruyor, ona dokunma."
