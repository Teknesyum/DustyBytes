# Avalonia Arayüz İnceleme Raporu

Kapsam: 7 açık kaynak deponun DustyBytes (Avalonia 11 + .NET 10, C#, AGPL-3.0-or-later) planına
göre incelenmesi. Görsel tema kitaplıkları yalnız fikir için okunur, PackageReference olamaz;
davranış kitaplıkları alınabilir.

## 1. AvaloniaUI/Avalonia — DERİN

- Lisans: `licence.md` — "The MIT License (MIT), Copyright (c) AvaloniaUI OÜ". AGPL-3.0-or-later
  ile sorunsuz (MIT izin verici, DustyBytes AGPL projesine bağımlılık olarak girebilir; yalnız
  MIT metni/telif bildirimi dağıtımda korunmalı — standart NuGet atıf zorunluluğu).

**Özel çizim (treemap için) — `ICustomDrawOperation` / `DrawingContext.Custom`.**
- Arayüz: `src/Avalonia.Base/Rendering/SceneGraph/CustomDrawOperation.cs` —
  `interface ICustomDrawOperation : IEquatable<ICustomDrawOperation>, IDisposable`; üyeler
  `Rect Bounds`, `bool HitTest(Point)`, `IntersectionResult HitTest(Geometry)`,
  `void Render(ImmediateDrawingContext context)`.
- Giriş noktası: `src/Avalonia.Base/Media/DrawingContext.cs:243` —
  `public abstract void Custom(ICustomDrawOperation custom)`; `Control.Render(DrawingContext)`
  içinden `context.Custom(new MyDrawOp(...))` çağrılır.
- Somut yol: `src/Avalonia.Base/Media/PlatformDrawingContext.cs:44-56` `Custom(...)` →
  `new ImmediateDrawingContext(_impl, false)` → `custom.Render(immediateDrawingContext)`;
  compositor yolunda `RenderDataDrawingContext.cs:141` `Custom(...) => Stream.DrawCustom(custom)`.
- Skia'ya iniş: `Render(ImmediateDrawingContext)` içinde
  `context.TryGetFeature<ISkiaSharpApiLeaseFeature>()`; Skia backend
  `src/Skia/Avalonia.Skia/DrawingContextImpl.cs:897-901` `GetFeature` → `SkiaLeaseFeature`;
  `ApiLease.SkCanvas` (satır 131) ham `SKCanvas`'ı döndürür —
  `using var lease = leaseFeature.Lease(); var canvas = lease.SkCanvas;` sonrası doğrudan
  `SKCanvas.DrawRect` vb. çağrılabilir.
- Çalışan örnek: `samples/RenderDemo/Pages/CustomSkiaPage.cs` (`CustomDrawOp` iç sınıfı, lease
  alıp `canvas.DrawPaint(...)`).
- **Etki:** Harita ekranı için tek bir `ICustomDrawOperation` sınıfı yazılıp binlerce
  `SKCanvas.DrawRect`/`DrawRoundRect` çağrısı tek draw-op'ta, tek GPU batch'inde yapılabilir —
  tek tek Avalonia `Rectangle` control'ü oluşturmaktan çok daha ucuz. Plan metnindeki
  "Skia `DrawingContext` ile tek çizim, hit-test elle" doğrudan bu mekanizmayla örtüşüyor
  (`HitTest(Point)` elle implemente edilir).

**Kendi başlık çubuğu — Win32 mekanizması.**
- Hint API'leri: `src/Windows/Avalonia.Win32/WindowImpl.cs:1578`
  `SetExtendClientAreaToDecorationsHint(bool)`, `:1586`
  `SetExtendClientAreaTitleBarHeightHint(double)` — ikisi de `ExtendClientArea()`'yı tetikler.
- `ExtendClientArea()` (satır 1213-1253): `DwmIsCompositionEnabled` kontrolü,
  `DwmExtendFrameIntoClientArea` ile frame client alana genişler, `UpdateExtendMargins()`
  (satır 1155) başlık yüksekliğini DWM'ye marj olarak bildirir,
  `SetNCRenderingPolicy(DWMNCRP_ENABLED)` native kenarlık/gölgeleri korur.
- Hit-test: `src/Windows/Avalonia.Win32/WindowImpl.CustomCaptionProc.cs` — `HitTestNCA`
  (satır 13) `WM_NCHITTEST`'e `HTCAPTION`/`HTTOP`/`HTLEFT` vb. döner; `CustomCaptionProc`
  (satır 91) sonucu `HTNOWHERE`/`HTCAPTION` ise `HitTestVisual(lParam)` (satır 234) ile
  `Win32Properties.GetNonClientHitTestResult` attached property'sini (ya da yeni
  `WindowDecorationsElementRole`: TitleBar/CloseButton/Maximize) sorar — uygulamanın kendi
  çizdiği kapat/küçült butonları gerçek `HTCLOSE`/`HTMINBUTTON` gibi davranır; Alt+F4,
  `WM_SYSCOMMAND`, sistem menüsü ve Windows 11 snap-layout native NC mesaj akışı korunduğu için
  otomatik çalışır.
- Örnek: `samples/ControlCatalog/Pages/WindowCustomizationsPage.xaml` (satır 22-24)
  `ExtendClientAreaEnabled` binding'li canlı demo; `samples/ControlCatalog/DecoratedWindow.xaml(.cs)`.
- **Etki:** `Window.ExtendClientAreaToDecorationsHint="True"` +
  `ExtendClientAreaTitleBarHeightHint` + `Win32Properties.SetNonClientHitTestResult` ile
  tamamen özel başlık çubuğu çizilebilir; Snap/sürükleme/Alt+F4 elle WndProc yazmadan,
  Win32 seviyesinde otomatik korunur — plan Bölüm 8'in "kendi başlık çubuğu (Snap, sürükleme,
  Alt+F4 geri verilir)" satırının tam karşılığı.

**Native AOT / Trimming.**
- `build/TrimmingEnable.props`: net8.0+ hedefleyen tüm projelerde `IsTrimmable=true`,
  `IsAotCompatible=true`, `EnableTrimAnalyzer=true`, `TrimmerSingleWarn=false` — çekirdek
  paketler trim/AOT uyumlu işaretli, derlemede trim-analiz uyarıları açık.
  `tests/BuildTests/BuildTests.NativeAot/BuildTests.NativeAot.csproj` (`net10.0`,
  `PublishAot=true`) ile CI'da gerçek NativeAOT publish test ediliyor.
- Bilinen kısıt: XAML derleyicisi ayrı paket (`Avalonia.Markup.Xaml.Loader`); reflection tabanlı
  dinamik XAML yükleme AOT'ta sorunlu olabilir — derlenmiş binding'ler
  (`AvaloniaUseCompiledBindingsByDefault`) ve compiled XAML kullanılmalı.
- **Etki:** `PublishAot=true` ile yayınlanabilir (plan A7: "ReadyToRun + trimming"); reflection
  tabanlı dinamik `DataTemplate` seçimi gibi desenlerden kaçınılmalı, trim uyarıları
  publish sırasında izlenmeli.

## 2. WalletWasabi/WalletWasabi — DERİN

- Lisans: `LICENSE.md` doğrulandı, **MIT** (`Copyright (c) 2026 The Wasabi Wallet
  Developers`). AGPL-3.0-or-later ile tam uyumlu — MIT kodu AGPL projesine dahil edilebilir;
  doğrudan kod alınırsa yalnız MIT bildirimi korunur.

**MVVM yapısı.** ReactiveUI (`ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`) üzerine
kurulu, CommunityToolkit.Mvvm kullanılmıyor.
- `WalletWasabi.Fluent/ViewModels/ViewModelBase.cs`: `ReactiveObject` + `INotifyDataErrorInfo`,
  `UiContext` alır, kendi `Validations` altyapısı property-changed'e bağlı.
- `WalletWasabi.Fluent/ViewModels/Navigation/RoutableViewModel.cs`: `ViewModelBase`'den türer;
  `BackCommand`/`CancelCommand`, `Title`, `IsBusy`, `OnNavigatedTo/From` yaşam döngüsü;
  navigasyon `UiContext.Navigate(target)` → `INavigationStack<RoutableViewModel>`.
- `NavigationStack.cs` / `TargettedNavigationStack.cs`: hedef bazlı (`NavigationTarget.
  HomeScreen`, `DialogScreen`, `CompactDialogScreen`) yığın tabanlı router; her view model
  kendi `DefaultTarget`'ını bildirir.
- `[AutoNotify]` kaynak üretici (`WalletWasabi.Fluent.Generators`) `INotifyPropertyChanged`
  boilerplate'ini otomatikleştiriyor.
- **Etki:** DustyBytes'ın 6 ekranı (Genel Bakış/Öneriler/Harita/Programlar/Temizlik/Karantina)
  için tek `ViewModelBase` + hedef enum + stack tabanlı router doğrudan örneklenebilir;
  ReactiveUI zorunlu değil, CommunityToolkit.Mvvm ile aynı iskelet kurulabilir.

**Animasyonlar.** `WalletWasabi.Fluent/Behaviors/DialogTransitionAttachedBehavior.cs`: dialog/
ekran geçişinde `Avalonia.Rendering.Composition` (`ElementComposition.GetElementVisual`) ile
`ImplicitAnimationCollection` kuruyor — yalnız `Opacity` (250ms) ve `Scale` transform (350ms),
easing `Easing.Parse("0.4,0,0.6,1")`. XAML `PageSlide`/`CrossFade` değil, GPU-compositor
tabanlı implicit animasyon.
- **Etki:** Planın "yalnız transform ve opacity" kuralıyla birebir örtüşüyor; bu
  compositor-visual deseni (behavior + ImplicitAnimationCollection) doğrudan model alınabilir,
  CPU-hafif ve XAML transition'dan daha performanslı.

**Ayrı süreç.** UI ve wallet/coinjoin mantığı **aynı process** içinde (`ProjectReference`,
IPC yok); `WalletWasabi.Backend`/`Coordinator` uzak sunucu, yerel ikinci süreç değil. Gerçek
ayrı-süreç örneği **Tor**: `WalletWasabi/Tor/TorProcessManager.cs` — `Process.Start` ile Tor
ikili dosyasını başlatır, iletişim named pipe değil **SOCKS5 TCP soket**. Tekil örnek kontrolü
`WalletWasabi.Client/SingleInstanceChecker.cs` — named pipe değil, `FileStream` +
`FileShare.None` kilit dosyası.
- **Etki:** DustyBytes'ın named-pipe arayüz/worker mimarisine doğrudan paralel örnek yok;
  WalletWasabi ayrıcalık ayrımı yapmıyor. Yalnız dış süreç başlatma deseni
  (`ProcessStartInfoFactory.cs`, `GracefulWaitForExitAsync`) referans olabilir — named-pipe
  IPC'nin kendisi .NET `System.IO.Pipes` standart yoldan, Polymerium örneğiyle (Bölüm 7)
  birlikte kurulmalı.

**Trimming/AOT.** Hiçbir csproj'da `PublishTrimmed`/`PublishReadyToRun`/`PublishSingleFile`
yok (`gh search code` boş); tek `PublishAot` referansı alakasız bir yardımcı script'te.
`WalletWasabi.Fluent.Desktop.csproj`'da yalnız `RuntimeIdentifiers` ve
`System.Globalization.Invariant=true`.
- **Etki:** WalletWasabi trimming/AOT konusunda örnek teşkil etmiyor (muhtemelen
  ReactiveUI/Avalonia reflection ağırlığı yüzünden kaçınılmış). DustyBytes'ın A7 aşamasındaki
  "ReadyToRun + trimming" hedefi bu depodan destek bulamıyor; Bölüm 1'deki Avalonia'nın kendi
  trimming durumuna bakılmalı.

## 3. kikipoulet/SukiUI — Yalnız Fikir

- Lisans: **MIT**. Ama plan kuralı gereği zaten PackageReference edilmeyecek; yalnız kaynak
  kod fikir için okunuyor.
- `SukiUI/Animations/` klasörü tema kodundan bağımsız, saf davranış (`AttachedProperty` +
  `Avalonia.Animation`) sınıfları içeriyor: `FadeInBehavior.cs`, `SquishyHoverBehavior.cs`,
  `HoverBehavior.cs`, `GlowBehavior.cs`, `SizeAnimationBehavior.cs`, `VisibilityBehavior.cs`,
  `SukiEasings.cs`, `SukiSpringEase.cs`.
- `FadeInBehavior.cs`: `EnableProperty`, `DurationProperty` (varsayılan 600ms),
  `ScaleProperty` (varsayılan 0.6) adlı `AttachedProperty`'ler; `EnableProperty.Changed`
  üzerinden kontrol görünür olduğunda `Opacity` + `ScaleTransform` ile fade+scale-in
  animasyonu tetikliyor. Yalnız `transform` (ScaleTransform) ve `opacity` kullanıyor —
  DustyBytes'ın "hareket temel, yalnız transform ve opacity" kuralıyla birebir örtüşüyor.
- `SquishyHoverBehavior.cs`: `ScaleTransform` + `SkewTransform` + `TranslateTransform`
  kombinasyonuyla hover'da "sıkışma" efekti; `DispatcherTimer` ile geri dönüş. Kart
  hover/tıklama geri bildirimi için doğrudan örnek alınabilecek bir desen (kod kopyalanmaz,
  yalnız AttachedProperty + Transform yaklaşımı model alınır).
- `SukiUI/Controls/SukiTransitioningContentControl.axaml(.cs)`: sayfa/tab içerik geçişinde
  crossfade+slide; Öneriler ekranındaki tür süzgeci geçişi için fikir kaynağı olabilir.
- **DustyBytes'a etki:** SukiUI paket olarak alınmaz (tema kitaplığı yasağı). Ama
  `AttachedProperty` + `ScaleTransform`/`Opacity` deseni, Bölüm 8'deki "yalnız transform ve
  opacity" kuralına uyan bir FadeIn/Squishy davranışının DustyBytes.App içinde **sıfırdan,
  kendi adlarıyla** yazılmasına referans olabilir — kart girişleri (Öneriler) ve buton/kart
  hover geri bildirimi için.

## 4. irihitech/Ursa.Avalonia — Davranış Kitaplığı Adayı

- Lisans: **MIT**, .NET Foundation destekli.
- **Uyarı:** Ursa'nın görselliği çalışması için `Semi.Avalonia` (bir görsel tema kitaplığı)
  paket referansı şart — README'nin "Get Started" adımı `Semi.Avalonia` +
  `Irihi.Ursa.Themes.Semi`'yi birlikte ekletiyor. Bu yüzden Ursa'yı doğrudan
  `Irihi.Ursa` + tema paketiyle almak, dolaylı yoldan bir tema kitaplığı sürüklemek
  olur ve plan kuralını ihlal eder.
- Kontrol seti geniş (`src/Ursa/Controls/`): `Drawer`, `Breadcrumb`, `Clock`, `Anchor`,
  `AspectRatioLayout`, `Banner`, `AutoCompleteBox`, `IconButton` grubu — bunların çoğu
  layout/davranış mantığı, temadan ayrılabilir C# `TemplatedControl` sınıfları.
- **DustyBytes'a etki:** Ursa paket olarak alınmaz (tema bağımlılığı riski). Ama tekil kontrol
  dosyaları (örn. `Breadcrumb.cs` → Harita ekranının "üstte yol kırıntısı" gereksinimi,
  `Drawer.cs` → Karantina/Programlar detay panelleri) **kod olarak okunup, DustyBytes kendi
  `--tk-*` token'larıyla yeniden yazılarak** fikir alınabilir. Doğrudan paket veya dosya
  kopyası önerilmez.

## 5. b-editor/beutl — Yoğun Özel Çizim Örneği

- Doğru depo adı **`b-editor/beutl`**'dir (`Beutl-Dev/Beutl` mevcut değil). Açıklama:
  "Cross-platform video editing (compositing) software." Lisans: **MIT**.
- `src/Beutl.Editor.Components/TimelineTab/Views/TimelineBackground.cs`: `TemplatedControl`'den
  türeyen, `override void Render(DrawingContext context)` içinde `for (double y = 0; y < height;
  y += itemHeight) context.DrawLine(...)` döngüsüyle timeline arka planına çok sayıda çizgi
  çiziyor. `AffectsRender<TimelineBackground>(...)` ile `StyledProperty` değişince yeniden çizim
  tetikleniyor.
- Bu, DustyBytes'ın Harita (treemap) ekranı için **ICustomDrawOperation/Skia yerine** basit
  `Control.Render(DrawingContext)` override'ının da (küçük-orta öğe sayısında) production'da
  kullanılan, çalışan bir desen olduğunu gösteriyor. Binlerce dikdörtgen için asıl referans
  yine de Avalonia'nın kendi `ICustomDrawOperation`'ı olmalı (bkz. Bölüm 1) — Beutl burada
  yalnız "büyük bir Avalonia uygulamasında custom Render nasıl kullanılıyor" örneği olarak işlev
  görüyor, treemap ölçeğinde performans kanıtı sunmuyor.
- **DustyBytes'a etki:** Harita ekranı ilk sürümde binlerce dikdörtgen çizecekse
  `ICustomDrawOperation` (Bölüm 1 bulgusu) tercih edilmeli; basit `Render` override deseni
  (Beutl örneği) yalnız düşük öğe sayılı, ikincil çizimler (örn. tarama ilerleme çubuğundaki
  "son dokuz günlük" mini-grafik) için yeterli olabilir.

## 6. Squarified Treemap Algoritması — DERİN

C# ekosisteminde MIT/uyumlu lisanslı, **gerçek** squarified treemap algoritması içeren aktif bir
depo bulunamadı. İki aday incelendi:

- **`menees/Treemap`** (`src/TreemapGenerator/Microsoft.Research.CommunityTechnologies.TreemapNoDoc/
  SquarifiedLayoutEngine.cs`): Microsoft Research'ün 2006 "Community Technologies" treemap
  bileşenlerinin bir "fork"u. Algoritma dosyası incelendi —
  `CalculateNodeRectangles` → `CalculateSquarifiedNodeRectangles` → `InsertNodesInRectangle`
  akışıyla klasik Bruls/Huizing/van Wijk squarify algoritmasını uyguluyor (satır satır: düğümler
  boyuta göre sıralanır, en iyi en-boy oranı bozulana kadar bir "sıraya" eklenir, oran bozulunca
  kalan boş alanla devam edilir). **Ancak lisansı `MSR-SSLA` (Microsoft Research Shared Source
  License) — yalnız ticari olmayan kullanım izinli, dağıtım ve türev çalışma AGPL ile taban
  tabana zıt.** Bu depodan **kod satırı bile alınamaz**, yalnız algoritmanın akış mantığı (genel
  bilgi, telif dışı) fikir amaçlı incelenebilir.
- **`d3/d3-hierarchy`** (`src/treemap/squarify.js`, ISC lisans — MIT'e denk, AGPL uyumlu):
  aynı Bruls et al. squarify algoritmasının kanonik, permissive-lisanslı referans uygulaması.
  `squarifyRatio` fonksiyonu: düğümler `value`'ya göre önceden sıralı gelir, `alpha/beta/minRatio`
  ile en-boy oranı izlenir, oran kötüleşince satır kapatılıp `treemapDice`/`treemapSlice` ile
  yerleştirilir. **AGPL uyumlu, algoritma mantığı doğrudan C#'a taşınabilir** (JS→C# çevirisi
  kod kopyası sayılmaz, algoritma matematiği evrenseldir; ayrıca ISC izin veriyor).
- **DustyBytes'a etki (Bölüm 8, Harita ekranı):** Treemap algoritması **`d3-hierarchy`'nin
  `squarify.js` mantığı temel alınarak DustyBytes.Core içinde C# olarak sıfırdan yazılmalı**;
  `menees/Treemap`'teki MSR-SSLA kod hiçbir şekilde kopyalanmamalı/portlanmamalı — yalnız genel
  algoritma akışını doğrulamak için okunabilir. Çizim tarafı Bölüm 1'deki
  `ICustomDrawOperation` ile birleştirilir: squarify C# tarafında dikdörtgen listesi üretir,
  tek bir custom draw operation bunları Skia'ya basar (plan: "tek çizim, hit-test elle").

## 7. Named Pipe / Tek Örnek (Single Instance) Deseni — d3ara1n/Polymerium

- Repo: **d3ara1n/Polymerium** ("A metadata-driven Minecraft launcher…"), Avalonia tabanlı,
  Lisans: **MIT**.
- `src/Polymerium.Avalonia/Facilities/SingleInstance.cs`: sınıf yorumunda Çince açık:
  "Named-Mutex 单实例守卫——第二实例经单向命名管道 ping 第一实例" (Named-Mutex tekil örnek
  bekçisi — ikinci örnek tek yönlü named pipe ile birinciyi "ping"ler).
  - `new Mutex(true, MUTEX_NAME, out createdNew)` ile ilk örnek tespiti (`IsFirstInstance`).
  - İlk örnek `StartServer()` ile `NamedPipeServerStream(PIPE_NAME, PipeDirection.In, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous)` döngüsünde dinler; gelen tek satırlık
    JSON (`Message { Action, Target, Args }`) deserialize edilip `Received` event'i tetiklenir.
  - İkinci örnek `Send(Message)` statik metoduyla `NamedPipeClientStream(".", PIPE_NAME,
    PipeDirection.Out, PipeOptions.Asynchronous)` açar, JSON satırı yazar, kapanır — "en iyi
    çaba" (best-effort): ilk örneğe ulaşılamazsa sessizce çıkar, kullanıcıyı bekletmez.
- **Sınırlama:** Bu depo yalnız **tek örnek (single-instance) + aktivasyon ping**'i çözüyor;
  DustyBytes'ın ihtiyacı olan **yükseltilmiş (`--worker`, yönetici) süreçle SID/PID doğrulamalı
  güvenli pipe köprüsü değil**. `PipeSecurity`, `NamedPipeServerStream` tarafında SID kısıtlaması
  ve `GetNamedPipeClientProcessId` ile karşı PID doğrulaması burada **yok** — DustyBytes planının
  "yalnız aynı kullanıcı SID'i, karşı PID doğrulanır" satırı için bu depodan ek güvenlik kodu
  yazılması gerekiyor, doğrudan alınamaz.
- **DustyBytes'a etki:**
  1. Tek örnek deseni (Mutex + tek yönlü pipe ping) **doğrudan model alınabilir** —
     DustyBytes.App açılışında ikinci başlatma zaten çalışan pencereyi öne getirmek için.
  2. Arayüz↔worker güvenli köprüsü (Bölüm "Köprü" kararı) için bu depo yalnız iskelet
     (NamedPipeServerStream/ClientStream kullanım şekli) verir; SID/PID doğrulama katmanı
     .NET `System.IO.Pipes.PipeSecurity` + Win32 `GetNamedPipeClientProcessId` P/Invoke ile
     DustyBytes.Worker içinde ayrıca yazılmalı.

## Plana Etkisi

| Bölüm | Değişiklik | Kaynak Depo |
|---|---|---|
| 8 Arayüz (Harita çizimi) | `ICustomDrawOperation.Render(ImmediateDrawingContext)` + `ISkiaSharpApiLeaseFeature` lease ile binlerce dikdörtgen tek draw-op'ta, tek GPU batch'te çizilir; `HitTest(Point)` elle | AvaloniaUI/Avalonia (`src/Avalonia.Base/.../CustomDrawOperation.cs`, `src/Skia/Avalonia.Skia/DrawingContextImpl.cs:897-901`) |
| 8 Arayüz (Başlık Çubuğu) | `ExtendClientAreaToDecorationsHint`+`ExtendClientAreaTitleBarHeightHint`+`Win32Properties.SetNonClientHitTestResult`; Snap/sürükleme/Alt+F4/sistem menüsü native NC akışıyla korunur | AvaloniaUI/Avalonia (`WindowImpl.cs:1213-1253,1578,1586`, `WindowImpl.CustomCaptionProc.cs:13,91,234`) |
| A7 Yayın (Trimming/AOT) | `PublishAot=true` ile yayın mümkün; reflection tabanlı dinamik binding/DataTemplate'ten kaçınılmalı | AvaloniaUI/Avalonia (`build/TrimmingEnable.props`, `tests/BuildTests/BuildTests.NativeAot`) |
| 8 Arayüz (Hareket) | Sayfa/kart geçişinde `AttachedToVisualTree` behavior + `Avalonia.Rendering.Composition` `ImplicitAnimationCollection` ile yalnız Opacity(250ms)+Scale(350ms) — XAML transition yerine compositor-visual deseni | WalletWasabi/WalletWasabi (`WalletWasabi.Fluent/Behaviors/DialogTransitionAttachedBehavior.cs`) |
| 8 Arayüz (Hareket, kart hover) | `AttachedProperty` + `ScaleTransform`/`Opacity` deseniyle kendi FadeIn/hover davranışı (kod kopyalanmaz, desen alınır) | kikipoulet/SukiUI (`SukiUI/Animations/FadeInBehavior.cs`, `SquishyHoverBehavior.cs`) |
| 8 Arayüz (Navigasyon/MVVM) | Tek `ViewModelBase` + hedef enum + yığın tabanlı `NavigationStack`; her ekran `OnNavigatedTo/From` ile kaynak temizler | WalletWasabi/WalletWasabi (`ViewModels/ViewModelBase.cs`, `Navigation/RoutableViewModel.cs`, `NavigationStack.cs`) |
| 8 Arayüz (Harita algoritması) | Squarified treemap C# implementasyonu `d3-hierarchy/squarify.js` (ISC) mantığından sıfırdan yazılır; `menees/Treemap`'in `SquarifiedLayoutEngine.cs` koduna hiç dokunulmaz (MSR-SSLA, AGPL ile uyumsuz) | d3/d3-hierarchy (fikir+algoritma), menees/Treemap (yalnız akış doğrulaması, kod yok) |
| Mimari (Süreç/Köprü) | Named-Mutex tek örnek + tek yönlü named pipe aktivasyon ping deseni model alınır; SID/PID doğrulama (`PipeSecurity`, `GetNamedPipeClientProcessId`) DustyBytes.Worker'da ayrıca yazılır | d3ara1n/Polymerium (`SingleInstance.cs`) |
| Mimari (Süreç başlatma) | Harici süreç başlatma deseni (`RedirectStandardOutput`, graceful exit bekleme) referans; WalletWasabi tek-process olduğundan named-pipe IPC'nin kendisi örnek değil | WalletWasabi/WalletWasabi (`Tor/TorProcessManager.cs`) |
| Açık Konular | Ursa.Avalonia paket olarak alınmaz (Semi.Avalonia'ya bağımlı); tekil kontrol dosyaları yalnız fikir için okunabilir | irihitech/Ursa.Avalonia |
