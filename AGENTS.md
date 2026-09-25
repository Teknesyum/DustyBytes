# DustyBytes

Windows disk temizleyici: tarar, büyük ve az kullanılanı amacına göre toplar (oyun, film,
program, klasör), karantinayla güvenli siler, programı kalıntısız kaldırır.

- Yığın: Avalonia 11 + .NET 10, C#, CsWin32. Arayüz yetkisiz, `--worker` yönetici.
- Plan: `docs/plan.md`. Yol haritası: `docs/YOL-HARITASI.md`. Danışma: `docs/danisma/`.
- Her sil/taşı isteği worker içinde korumalı listeden geçer; arayüz dosyaya dokunmaz.
- Doğrudan silme yok: karantina ya da kategori politikası.
- Renk ve ölçü yalnız teknesyum-ui token'larından; kabuk standardı testlerle ölçülür.
- Git'e giden her şey İngilizce; proje içi kağıtlar Türkçe. Lisans AGPL-3.0-or-later.
- Geçici dosya `tmp/`, işi biten dosya `trash/`.
