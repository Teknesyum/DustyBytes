# Teknesyum-UI Bildirimi — Düzen Seçimleri Projeye Geçmiyor

Kaynak proje: DustyBytes (Avalonia), `uc` denetimi 2026-09-27. Gönderildi: "Standart renkleri iyileştir"
oturumu (Teknesyum-UI) ve GitHub issue.

## Belirti

Sahip önizlemede düzenini kurdu, projede `setup.js --apply` ve `uc` koştu; uygulama önceki
görünüşünde kaldı. Denetim "0 kontrast hatası, 0 tarayıcı hatası" ile geçti.

## Kaynak

1. `~/.claude/teknesyum-private/teknesyum-ui/onizleme/ayarlar.json` → `fark` alanı sahibin
   seçimlerini taşıyor. Yalnız renk, köşe ve süre değerleri `benim.tokens.json`'a geçiyor.
2. Kalan seçimler `benim.notlar.json`'a düz metin olarak yazılıyor ("token karşılığı yok").
3. `scripts/setup.js:95` bu notları okuyup `setup.js:710` ile config'e `notlar` diye kopyalıyor;
   hiçbiri token'a, `Theme.axaml`'a ya da şablon temalarına dönüşmüyor.
4. Şablon temaları token yerine elle değer taşıyor: `avalonia/Theme.axaml` Panel `CornerRadius="6"`
   (satır ~316), düğme şablonu `CornerRadius="6"` (satır ~374), üst çubuk `TitleBarHeightMax` (40).
5. `scan.js` ve `denetim` (KontrastTests) bu seçimleri hiç ölçmüyor; denetim görünüş farkını
   yakalayamıyor, "geçti" diyor.

## Seçim → Projede Görülen

| `ayarlar.json` | Üretilen tema |
|---|---|
| `arka.tur: duz` | 16 duraklı, 48 sn dönen gradyan (`AppBgGradient`, `Panel.appbg`) |
| `pencere.cubuk: 28` | Üst çubuk 40 px (`TitleBarHeightMax`) |
| `dugme: h 28, px 10` | Düğme 40 px, dolgu 12 (`InputHeight`, `InputPadding`) |
| `sekil.r: 3` | Token 3; Panel ve düğme temasında elle 6 |
| `arayuzEgri: emphasized` | `EOut` = 0.2,0,0,1 |
| `sureCarpan: 0.5` | Süreler 40/80/150/240, çarpan yok |
| `golge: 0` | `shadow-panel` alpha 0.8, blur 40 |
| `pencere.kenar: yok` | Doğrulanmadı |
| `kaydir: 3, solan` | Doğrulanmadı |
| `cam: 0` | Uygulanmış (blur 0) |

## İstenen Kapanış (tekrarlanmayacak biçimde)

1. `ayarlar.json` `fark` alanındaki her seçim bir token'a ya da üretilen tema kaynağına dönüşür;
   "token karşılığı yok" notu kalmaz. Karşılığı olmayan seçim için token eklenir.
2. Şablon temalarında elle yazılmış ölçü, köşe, süre, eğri kalmaz; hepsi token'a bağlanır.
3. Tekrarı önleyen kapı: `ayarlar.json` ile üretilen `Theme.axaml` / `theme.css` değerlerini
   karşılaştıran bir denetim (scan kuralı ya da `denetim` testi). Fark varsa kırmızı.
   `setup.js --apply` sonunda bu kapı koşar; `uc` bitiş ölçütüne "düzen eşleşmesi 0 fark" girer.
4. `ui-denetim` kitabına: "kullanılabilir" kanıtı önizleme görüntüsüyle uygulama görüntüsünün
   yan yana kıyası içerir.
5. Düzeltme sonrası DustyBytes'ta `setup.js --apply` yeniden koşar; sonuç bu projede doğrulanır.
