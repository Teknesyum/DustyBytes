# DustyBytes — Kod İmzasız Dağıtımda SmartScreen ve Ucuz İmza Sertifikaları Araştırması

Tarih: 2026-09-25. Web araştırmasıyla hazırlandı, tüm iddiaların yanında kaynak linki var.
Doğrulanamayan / çelişkili bilgiler ayrıca işaretlendi.

---

## A) İmzasız kalırsak SmartScreen'i "yine de çalıştır" dedirtmeden aşmanın yolları

### A.1 Mark-of-the-Web (MOTW / Zone.Identifier) mekaniği

- Dosya internetten indirilince tarayıcı, `Zone.Identifier` adlı bir Alternate Data Stream (ADS)
  ekler (`ZoneId=3`). SmartScreen ve "Açarken uyar" davranışı bu işarete bakar.
  Kaynak: [text/plain — Downloads and the MOTW](https://textslashplain.com/2016/04/04/downloads-and-the-mark-of-the-web/)
- **Zip içinden çıkan exe MOTW alır mı, arşivleyiciye bağlı:**
  - **Windows'un yerleşik "Tümünü Çıkar" özelliği**: MOTW'u çıkarılan dosyalara **taşır** (resmi
    olarak doğrulanmış bir Microsoft dokümanı bulunamadı, ancak güvenlik topluluğunda ve
    forumlarda yaygın kabul gören davranış budur — **doğrulanamadı, ikincil kaynaklara dayanıyor**).
  - **7-Zip 22.00+**: "Propagate Zone.Id stream" ayarı var; **varsayılan olarak Hayır** durumundadır,
    yani kullanıcı elle açmazsa MOTW **taşınmaz** → SmartScreen tetiklenmez.
    Kaynak: [BleepingComputer — 7-Zip now supports MOTW](https://www.bleepingcomputer.com/news/microsoft/7-zip-now-supports-windows-mark-of-the-web-security-feature/)
  - 7-Zip'te MOTW taşıma mantığındaki hatalar CVE olarak raporlanmış (ör. CVE-2025-0411,
    CVE-2026-58052) — yani 7-Zip'in MOTW'u atlaması **güvenlik açığı** sayılıyor, bilinçli tercih değil.
    Kaynak: [GitHub Advisory — CVE-2026-58052](https://github.com/advisories/ghsa-fx33-p83c-vpr5),
    [oss-security — CVE-2025-0411](https://www.openwall.com/lists/oss-security/2025/01/24/6)
- **Sonuç:** Zip'i 7-Zip'in varsayılan ayarıyla dağıtmak, birçok kullanıcı için SmartScreen'i
  fiilen **atlatır** — ama bu resmî bir "yol" değil, 7-Zip'in eksik/hatalı davranışına dayanıyor;
  Microsoft bunu güvenlik açığı olarak görüyor ve gelecekte kapatılabilir. **Kullanıcıya "çalıştır"
  dedirtmeme aracı olarak önermek riskli ve etik açıdan tartışmalı** (aslında kullanıcıyı
  korumasız bırakmak).

### A.2 `Unblock-File`

- PowerShell'de `Unblock-File -Path .\DustyBytes.exe` komutu MOTW akışını siler.
  Kaynak: [Markdown Monster blog — SmartScreen dealing](https://markdownmonster.west-wind.com/blog/posts/2026/Jun/01/Windows-Protected-your-PC-Dealing-with-Windows-SmartScreen-on-Installation)
- **Kim çalıştırır?** Kullanıcının kendisi, elle. Yani bu da "yine de çalıştır" tıklamasının
  yerine geçen ayrı bir manuel adım — kullanıcı deneyimi açısından SmartScreen ekranından
  farksız, hatta daha teknik ve kafa karıştırıcı. **Otomatik/sessiz bir çözüm değil.**
- Maliyet: sıfır. Dezavantaj: kullanıcıya "PowerShell komutu çalıştır" demek, "yine de çalıştır"a
  tıklatmaktan daha kötü bir UX ve güven sinyali.

### A.3 winget ile dağıtım

- winget paketleri tarayıcı üzerinden inmediği için MOTW genelde **hiç eklenmiyor** —
  paket yöneticisi indirmeyi kendi işliyor, IAttachmentExecute servisi devreye girmiyor.
  Kaynak: [Compass Security — WinGet Desired State](https://blog.compass-security.com/2026/03/winget-desired-state-initial-access-established/)
- Ama garanti değil: bazı winget kurulumlarında MOTW/AttachmentExecute adımında takılıp
  `Unblock-File` gerektiren vakalar raporlanmış.
  Kaynak: [Markdown Monster blog](https://markdownmonster.west-wind.com/blog/posts/2026/Jun/01/Windows-Protected-your-PC-Dealing-with-Windows-SmartScreen-on-Installation)
- **winget-pkgs deposuna kabul şartı**: Microsoft, winget-pkgs'e giren installer'ların
  **imzalı olmasını zorunlu tutmuyor** ama gönderim sırasında Defender taraması yapılıyor;
  imzasız pakette SmartScreen riski hâlâ community/manifest seviyesinde tartışma konusu
  (**doğrulanamadı** — winget-pkgs'in imza zorunluluğu resmi dokümanla teyit edilemedi, sadece
  pratik gözlemler bulundu).
- **Maliyet:** winget-pkgs'e manifest PR açmak ücretsiz, ama inceleme/onay süreci var, her
  sürümde manifest güncellemesi gerekiyor.
- **Dezavantaj:** Kullanıcı yine de zip'i GitHub Releases'ten indirirse (asıl dağıtım kanalı)
  MOTW ve SmartScreen aynen çalışır. winget sadece **alternatif** bir kanal, ana sorunu çözmez.

### A.4 Microsoft Store (MSIX)

- 2025'ten itibaren bireysel geliştirici kaydı **ücretsiz** (önceden ~$19 tek seferlikti).
  Kaynak: [Windows Developer Blog — Free developer registration](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)
- MSIX paketi Store'a gönderilince **Microsoft kendi sertifikasıyla imzalıyor** — ayrı bir
  kod imzalama sertifikası almaya gerek yok, ücretsiz.
  Kaynak: [Microsoft Learn — Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- Store'dan kurulan uygulamalarda SmartScreen/MOTW akışı devreye girmiyor (Store kendi
  güven zincirini kullanıyor) — **uyarı tamamen kalkıyor**.
- **Dezavantaj:** Store inceleme süreci, sertifikasyon süresi var; sadece Store'dan indirenler
  için geçerli — GitHub Releases'ten zip indiren kullanıcı hâlâ SmartScreen görür. Store
  politikaları (adware/telemetri kuralları, sürüm inceleme SLA'sı) ek yük getirir. AGPL lisanslı
  bir açık kaynak proje için Store'a koymak mümkün ama ek bir dağıtım kanalı işi.

### A.5 Scoop / Chocolatey

- Paket yöneticileri "tarayıcıdan indirilen exe → MOTW" adımını atladığı için SmartScreen'i
  fiilen azaltıyor, ama **hiçbiri MOTW/SmartScreen/AV uyarılarını bastırmayı garanti etmiyor**.
  Kaynak: [win-update-checker / genel gözlem](https://github.com/adrian3092/win-update-checker)
- Chocolatey'de imzasız/topluluk paketleri bazen AV tarafından yanlış pozitif alıyor
  (ör. Get-ChocolateyWebFile ESET tarafından malicious işaretlenmiş).
  Kaynak: [chocolatey/choco #3423](https://github.com/chocolatey/choco/issues/3423)
- **Sonuç:** winget ile aynı kategori — yardımcı ama ana sorunu (GitHub Releases'ten indirilen
  zip) çözmüyor.

### A.6 SmartScreen itibarı (reputation) — imzasız dosyada mümkün mü?

- Evet, SmartScreen dosya **hash'ine** göre bulut itibarı biriktiriyor; imza şart değil ama
  imzasız dosyada itibar **sıfırdan** başlıyor ve her yeni sürüm (her yeni hash) itibarı
  sıfırlıyor. Haftalarca, yüzlerce temiz indirme/çalıştırma gerekebiliyor.
  Kaynak: [Microsoft Learn — SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation),
  [text/plain — Best Practices for SmartScreen AppRep](https://textslashplain.com/2024/11/15/best-practices-for-smartscreen-apprep/)
- Sık sürüm çıkaran bir proje için bu pratikte hiç işe yaramaz — her release yeni hash, itibar
  sıfırlanır. DustyBytes gibi aktif geliştirilen bir proje için **kullanılamaz** bir strateji.

### A.7 Kurulum betiği (`irm ... | iex`)

- `Invoke-WebRequest`/`Invoke-RestMethod` ile indirilen dosyalarda MOTW **genelde eklenmiyor**
  (tarayıcı/IAttachmentExecute devrede değil).
  Kaynak: [PS-MOTW GitHub](https://github.com/nmantani/PS-MOTW), genel gözlem — **kısmen doğrulanamadı**,
  resmi Microsoft kaynağı yok, sadece güvenlik araştırmacılarının gözlemi.
- Bu tam olarak kötü amaçlı yazılımların ve "activation script"lerin (ör. MAS) kullandığı yöntem —
  MOTW'u tamamen atlatmak için. Kaynak: [SonicWall — Living Off Legit Tools](https://www.sonicwall.com/blog/living-off-legit-tools-stealthy-installation-of-remote-monitoring-agents-using-smartscreen-bypass)
- **Kritik dezavantaj:** Bu yöntemi meşru bir açık kaynak proje için önermek, kullanıcıyı
  "SmartScreen'i bilerek atlatan" bir davranışa yönlendirmek demek — hem güven kaybı hem de
  antivirüs/EDR ürünlerinin bu deseni (irm|iex + MOTW yokluğu) **kötü amaçlı davranış imzası**
  olarak işaretleme riski var. Önerilmez.

### A.8 Kaynak koddan derleme

- MOTW ve SmartScreen tamamen devre dışı kalır çünkü kullanıcı hiç "internetten indirilen
  çalıştırılabilir dosya" açmıyor, kendi derliyor. **%100 çalışır ama** hedef kitle (sıradan
  Windows kullanıcısı, disk temizleyici arıyor) için pratik değil — .NET SDK kurulumu, build
  adımları gerektirir. Sadece geliştirici/ileri kullanıcı kitlesi için bir seçenek.

### A.9 Microsoft Defender / SmartScreen dosya gönderim portalı

- `Report this file as safe` / Microsoft Security Intelligence submission portalı üzerinden
  dosya gönderilebilir; Microsoft "24-48 saatte" güncelleme yapabildiğini söylüyor ama pratikte
  tutarsız — bazı biletler haftalarca "In progress" kalıp sessizce kapanmış.
  Kaynak: [text/plain — Security Software False Positives](https://textslashplain.com/2026/01/27/microsoft-defender-false-positives/)
- **Kritik nokta:** Bu mekanizma **AV/Defender yanlış pozitiflerini** düzeltmek için; SmartScreen'in
  "bu dosya bilinmiyor, imzasız" uyarısı bir "false positive" değil, **beklenen davranış** —
  yani submission portalı SmartScreen'in "İmzasız/az bilinen dosya" uyarısını kaldırmıyor,
  sadece dosya kötü amaçlı diye yanlış işaretlenmişse düzeltiyor. **İşe yaramaz, farklı bir sorunu çözüyor.**

### A.10 Smart App Control (Windows 11) — KRİTİK

- SAC, imza **ve** itibara bakan ayrı bir katman; **imzasız + itibarsız exe'yi doğrudan
  engelliyor**, "yine de çalıştır" seçeneği **yok** (SmartScreen'in aksine).
  Kaynak: [AskVG — Smart App Control blocked](https://www.askvg.com/fix-smart-app-control-has-blocked-this-app-error-in-windows-11/),
  gerçek vaka: [logseq/og #55](https://github.com/logseq/og/issues/55)
- Kullanıcı tarafında tek çözüm SAC'ı **tamamen kapatmak** (sistem ayarları) — bu da eskiden
  yeniden kurulum gerektiriyordu, yakın zamanda Windows Güvenliği üzerinden geri açılabilir hale
  geldi ama yine de sistem geneli bir ayar değişikliği, uygulama bazlı istisna yok.
- **DustyBytes için anlamı:** SAC etkinse (Windows 11'in yeni kurulumlarında **varsayılan açık**),
  imzasız DustyBytes.exe SmartScreen ekranı bile göstermeden **tamamen engellenebilir** —
  "yine de çalıştır" seçeneği hiç çıkmaz. Bu, imzasız dağıtımın en büyük riski.
  Kaynak: [Microsoft Q&A — SAC blocks signed app too](https://learn.microsoft.com/en-us/answers/questions/5791210/application-signed-and-blocked-by-smart-app-contro)
  (hatta bazı imzalı ama itibarsız uygulamalar da engellenebiliyor).

---

### Öneri sırası (A sorusu)

1. **Gerçek çözüm imza almak** — SAC'ı da SmartScreen'i de kalıcı çözen tek yol bu (bkz. B).
   SignPath Foundation (açık kaynak için ücretsiz) veya ucuz bir OV sertifika + imzalama.
2. İmza gelene kadar/yanında: **Microsoft Store'a MSIX olarak da yayınla** — ücretsiz, o kanaldan
   inen kullanıcı için sorun tamamen kalkar.
3. **winget-pkgs'e ekle** — ücretsiz, ek kanal, MOTW riskini azaltır ama garanti etmez.
4. README'de **Unblock-File** talimatı ver (SAC kapalıysa işe yarar, ama zaten SmartScreen
   ekranındaki "Yine de çalıştır" kadar kolay, ekstra fayda sınırlı).
5. **7-Zip'in MOTW atlamasına güvenme** — bu bilinçli bir strateji değil, kapatılabilecek bir
   hata. Kullanıcıyı bilgilendirmeden "uyarıyı gizlice atlatmak" AGPL açık kaynak projenin
   şeffaflık ilkesiyle de çelişir.
6. **`irm | iex` kurulum betiği ve MOTW'u elle silme önerisini kullanma** — güven ve güvenlik
   açısından zararlı, AV/EDR tarafından kötü amaçlı desen olarak görülme riski var.
7. Reputation'ın kendiliğinden birikmesini beklemek DustyBytes gibi sık sürüm çıkan bir proje
   için **pratik değil**.

**En kritik uyarı:** Smart App Control açık olan (yeni Windows 11 kurulumlarının çoğu) bir
sistemde imzasız exe hiç çalışmayabilir — kullanıcıya "yine de çalıştır" seçeneği bile
çıkmayabilir. Bu tek başına imza almayı neredeyse zorunlu kılıyor.

---

## B) Ucuz kod imzalama sertifikaları — güncel fiyatlar (Eylül 2026)

### B.1 Karşılaştırma tablosu

| Sağlayıcı / Ürün | Yıllık toplam maliyet | Bireysel başvuru | SmartScreen hemen mi kalkıyor | CI/CD (GitHub Actions) otomasyonu | Kaynak |
|---|---|---|---|---|---|
| **Certum Open Source Code Signing** (bulut) | **€49/yıl** (SimplySign bulut, donanım yok) | Evet — açık kaynak proje sahibi bireyler için tasarlanmış | Hayır, itibar birikimi gerekir (2024 sonrası EV/OV farksız) | Kısmi — SimplySign Desktop/CLI ile mümkün, GitHub Actions entegrasyonu resmi değil, topluluk çözümleri var | [Certum Shop](https://shop.certum.eu/code-signing.html) |
| **Certum Open Source Code Signing** (kart+okuyucu set) | **€69** ilk yıl + kargo, sonraki yıl ~€25-29 (elektronik kod) | Evet | Hayır (itibar bekler) | Zor — fiziksel token, CI'da headless kullanım külfetli | [Certum Shop](https://shop.certum.eu/code-signing.html), [piers.rocks blog (Ekim 2025 deneyimi)](https://piers.rocks/2025/10/30/certum-open-source-code-sign.html) |
| **Certum Standard Code Signing** (bulut) | **€209/yıl** | Evet (IV — bireysel doğrulama) | Hayır | Kısmi (yukarıdaki gibi) | [Certum Shop](https://shop.certum.eu/code-signing.html) |
| **Azure Trusted Signing / Artifact Signing** | **$9.99/ay ≈ $120/yıl** (Basic, 5.000 imzaya kadar) | **Sadece ABD ve Kanada'da bireysel geliştiriciler** kabul ediliyor (2026 GA sonrası; AB/UK sadece işletme) | Hayır, itibar bekler ama Microsoft altyapısı olduğu için hızlanabildiği iddia ediliyor (**doğrulanamadı**) | **Evet, resmi** — Azure/trusted-signing-action GitHub Action'ı var | [Azure Pricing](https://azure.microsoft.com/en-us/pricing/details/trusted-signing/), [Microsoft Q&A — bireysel/ülke kısıtı](https://learn.microsoft.com/en-us/answers/questions/5595324/i-signed-up-to-generate-certificates-to-sign-my-co) |
| **SSL.com eSigner OV (cloud)** | En düşük tier **$180/yıl** (240 imza/yıl, %25 indirimle) | Evet, IV/OV bireysel doğrulama kabul ediyor | Hayır | Evet — CodeSignTool CLI + resmi GitHub Action var | [SSL.com eSigner pricing](https://www.ssl.com/guide/esigner-pricing-for-code-signing/) |
| **SSL.com OV (USB token, cloud olmayan)** | ~**$64.50/yıl**'dan başlıyor (kaynak taraması, tam şartlar teyit edilemedi) | Evet | Hayır | Token fiziksel ise zor | [SSL.com OV ürün sayfası](https://www.ssl.com/products/software-integrity/code-signing/ov/) — **fiyat doğrulanamadı, üçüncü taraf özetinden** |
| **Sectigo/Comodo OV (en ucuz bayi)** | **~$216-226/yıl** (SignMyCode $215.99, CodeSignCert $226.10) | Evet, bayiler bireysel/IV doğrulama sunuyor | Hayır | Bayiye göre değişir, genelde signtool ile manuel/CI scriptlenebilir | [SignMyCode](https://signmycode.com/sectigo-code-signing), [CodeSignCert](https://codesigncert.com/sectigo-code-signing-certificate) — **bayi fiyatları resmi Sectigo listesi değil, doğrulanamadı** |
| **SignPath Foundation** | **$0** (açık kaynak projeler için tamamen ücretsiz) | Proje bazlı (kişi değil) başvuru — OSI onaylı lisans, herkese açık repo, aktif bakım şartı | Hayır, itibar bekler ama Microsoft'un kendi Trusted Signing altyapısını da destekleyebiliyor (**kısmen doğrulanamadı**) | Evet — resmi GitHub entegrasyonu (SignPath App) var, CI'da otomatik imzalama tasarlanmış | [SignPath Foundation şartları](https://signpath.org/terms.html), [SignPath open source çözümü](https://signpath.io/solutions/open-source-community) |
| **GlobalSign / DigiCert (karşılaştırma)** | Genelde **$300-470/yıl** aralığında (liste fiyatları, teyit edilemedi) | Evet ama kurumsal odaklı, süreç daha ağır | Hayır | Var, ama pahalı ve kurumsal | **Doğrulanamadı** — bu araştırmada resmi fiyat sayfaları taranmadı, sadece dolaylı referanslar bulundu |

### B.2 EV vs OV — 2024 sonrası itibar farkı

- **Doğrulandı:** 2024'ten itibaren Microsoft, EV sertifika ile imzalanmış dosyalara artık
  **anında SmartScreen itibarı vermiyor**. Eskiden EV imzası ilk indirmede bile uyarıyı
  kaldırırdı; bu davranış kaldırıldı, EV de OV gibi itibarı organik biriktirmek zorunda.
  Kaynak: [ToDesktop blog — EV certs do not grant immediate reputation anymore](https://www.todesktop.com/blog/posts/windows-apps-psa-ev-certs-do-not-grant-immediate-reputation-anymore),
  [DigiCert Knowledge — EV-signed app still shows SmartScreen warnings](https://knowledge.digicert.com/alerts/ev-signed-application-showing-microsoft-defender-smartscreen-warnings)
- EV'nin kalan avantajları: yayıncı adının "doğrulanmış" görünmesi, kernel-mode driver imzalama
  zorunluluğu (DustyBytes'ın worker'ı driver değil, bu geçerli değil).
- **Sonuç: DustyBytes için EV almaya gerek yok — OV/IV yeterli, fiyat farkı (EV genelde
  3-5 kat pahalı) haklı çıkmıyor.**

### B.3 Bireysel geliştirici + Türkiye özeti

- **Certum**: Açık kaynak sertifikası tam olarak bireysel geliştiriciler için var; Certum'un
  kendi mağazası (shop.certum.eu) uluslararası satış yapıyor, Türkiye'den kart ile ödeme kabul
  ediyor gibi görünüyor (**tam teyit edilemedi** — sipariş akışı test edilmedi). Türkiye'de
  yerel bayi de var (certum.com.tr) ama bu bayi öncelikle EV/kurumsal odaklı görünüyor.
- **Azure Trusted Signing**: Bireysel geliştirici başvurusu **sadece ABD/Kanada** ile sınırlı —
  Türkiye'den bireysel başvuru **mümkün değil** (mevcut belgelere göre).
- **SSL.com / Sectigo bayileri**: Kimlik/IV doğrulaması dünya genelinde kabul ediyor, Türkiye
  dahil — pasaport/kimlik + bazen video doğrulama istiyorlar (**detaylar doğrulanamadı**).
- **SignPath Foundation**: Coğrafi kısıtlama yok, proje OSI lisanslı ve herkese açık olduğu
  sürece başvurabilir — **DustyBytes AGPL-3.0-or-later ile uygun görünüyor**, en cazip seçenek.

### Öneri sırası (B sorusu)

1. **SignPath Foundation'a başvur** — ücretsiz, AGPL açık kaynak projesi için tasarlanmış tam
   isabet, CI entegrasyonu hazır. Tek risk: başvuru/onay süreci ve "aktif bakım" şartının
   sürekli sağlanması gerekliliği.
2. Paralel/yedek: **Certum Open Source Code Signing (bulut, €49/yıl)** — düşük maliyet, hızlı
   başlangıç, SignPath onayı gecikirse veya reddedilirse devreye girer.
3. CI otomasyonu öncelikliyse ve bütçe izin veriyorsa **Azure Trusted Signing** cazip ama
   **Türkiye'den bireysel başvuru şu an kapalı** — bu yüzden DustyBytes için pratikte elenir.
4. EV sertifikaya **gerek yok** — 2024 sonrası itibar avantajı kalmadı, maliyeti haklı çıkmıyor.

---

## Doğrulanamayan noktaların listesi (özet)

- Windows'un yerleşik "Tümünü Çıkar" özelliğinin MOTW'u taşıyıp taşımadığı — resmi Microsoft
  kaynağı bulunamadı.
- winget-pkgs deposunun imzasız installer kabul politikası.
- `irm | iex` ile inen dosyalarda MOTW'un kesin olarak hiç eklenmediği — sadece ikincil kaynak.
- SSL.com'un $64.50/yıl rakamının tam olarak hangi ürün/koşula ait olduğu.
- Sectigo/Comodo bayi fiyatlarının resmi liste fiyatı mı yoksa promosyon mu olduğu.
- GlobalSign/DigiCert güncel liste fiyatları (bu araştırmada taranmadı).
- Certum'un uluslararası mağazasından Türkiye'den bireysel kart ile ödeme akışının sorunsuz
  çalıştığı.
- Azure Trusted Signing'in itibar birikimini hızlandırdığı iddiası.
