# Birimler — A2+A3 Birim Çıkarıcılar

Kapsam: `docs/plan.md` §3 (Birimler — Toplu Teklif) ve §4 (Puanlama). Taranan dosya ağacını
amacına göre (oyun, film, dizi, geliştirici artığı, önbellek, kurulum dosyası, sistem artığı,
büyük-eski klasör) tekil `Unit` tekliflerine dönüştüren katman.

## Yazılan Dosyalar

**Kurallar**
- `rules/dev-artifacts.json` — kondo (MIT, tbillington/kondo) kaynağından doğrulanmış 23
  ekosistem: Cargo, Node (+React Native alt dizinleri dahil), Unity, Stack, Cabal, SBT,
  Maven, Gradle, CMake, Unreal, Jupyter, Python, Pixi, Composer, Pub (Dart/Flutter), Elixir,
  Swift, Zig, Godot 4.x, .NET, Turborepo, Terraform, CocoaPods.

**`src/DustyBytes.Units/`**
- `UnitContext.cs` — `UnitContext` record (ScanResult, IUsageIndex, ProtectedList, Now).
- `IUnitExtractor.cs` — `IEnumerable<Unit> Extract(UnitContext ctx)`.
- `UnitIdentity.cs` — kararlı `Unit.Id` üretimi (kind + birincil yol SHA256'sı).
- `PathPattern.cs` — joker segment eşleştirme, göreli yol çözümleme, marker glob eşleştirme.
- `DevArtifactRules.cs` — ekosistem kuralları modeli + `System.Text.Json` kaynak üreteci ile yükleme.
- `DevArtifactExtractor.cs`, `CacheExtractor.cs`, `BrowserCacheExtractor.cs`,
  `InstallerExtractor.cs`, `GameExtractor.cs`, `FilmExtractor.cs`, `SeriesExtractor.cs`,
  `SystemArtifactExtractor.cs`, `LargeOldFolderExtractor.cs` — dokuz çıkarıcı.
- `UnitBuilder.cs` — tüm çıkarıcıları çalıştırır, önceliğe göre tekilleştirir, korumalı liste
  ve bulut/reparse dışlamasını uygular, puanlar, sıralar.
- `LargeFileList.cs` — salt okunur, büyük ve eski `LastWrite`'lı dosya listesi (A1 için).

**`tests/DustyBytes.Units.Tests/`**
- `TestSupport.cs`, `DevArtifactExtractorTests.cs`, `CacheExtractorTests.cs`,
  `BrowserCacheExtractorTests.cs`, `InstallerExtractorTests.cs`, `GameExtractorTests.cs`,
  `FilmExtractorTests.cs`, `SeriesExtractorTests.cs`, `SystemArtifactExtractorTests.cs`,
  `LargeOldFolderExtractorTests.cs`, `UnitBuilderTests.cs`, `LargeFileListTests.cs`.
- `DustyBytes.Units.Tests.csproj` — `DustyBytes.slnx`'e eklendi.

`tests/fixtures/units/` kullanılmadı: tüm ağaçlar testlerde elle `Tree.Dir`/`Tree.File` ile
kuruluyor, gerçek dosya fikstürüne ihtiyaç yok. `rules/units.json` yazılmadı: `Unit`/
`Scoring` modeli Core'da zaten var, `dev-artifacts.json` tek ek kural dosyası olarak yeterli.

## Genel API

```csharp
public interface IUnitExtractor { IEnumerable<Unit> Extract(UnitContext ctx); }

public sealed record UnitContext
{
    public required ScanResult ScanResult { get; init; }
    public required IUsageIndex UsageIndex { get; init; }
    public required ProtectedList Protected { get; init; }
    public required DateTimeOffset Now { get; init; }
    public ScanNode Root => ScanResult.Root;
}

public static class UnitBuilder
{
    public static IReadOnlyList<Unit> Build(UnitContext ctx);
    public static IReadOnlyList<Unit> Build(UnitContext ctx, IReadOnlyList<IUnitExtractor> extractors);
}

public sealed record LargeFileEntry(string Path, long SizeBytes, DateTimeOffset LastWrite);
public static class LargeFileList
{
    public static IReadOnlyList<LargeFileEntry> Build(ScanNode root, int take = 100);
}
```

Çıkarıcı sınıfları: `DevArtifactExtractor(DevArtifactRules? rules = null)`, `CacheExtractor()`,
`BrowserCacheExtractor()`, `InstallerExtractor()`, `GameExtractor()`, `FilmExtractor()`,
`SeriesExtractor()`, `SystemArtifactExtractor()`, `LargeOldFolderExtractor()` — hepsi
`IUnitExtractor` uygular, parametresiz kurulabilir.

`UnitBuilder` tekilleştirme önceliği: Game > Program > DevArtifact > BrowserCache > Cache >
Installer > Series > Film > SystemArtifact > Folder. `SystemArtifact` dışındaki her birim
korumalı listeden (`ProtectedList.CheckPath`) geçmek zorunda; bulut yer tutucu veya reparse
point bayraklı yollar her zaman elenir.

## Test Çıktısı

```
dotnet test tests/DustyBytes.Units.Tests
Başarılı! - Başarısız: 0, Başarılı: 35, Atlanan: 0, Toplam: 35
```

Kapsanan senaryolar: her çıkarıcı için pozitif/negatif örnekler; `node_modules` bir Film'e
uygun klasörün içindeyken DevArtifact'ın kazanması (öncelik testi); Downloads'taki kurulum
dosyası + açılmış kopya güven artışı; dizi deseni pozitif (`S01E01`, `1x01`) ve negatif
(`Serie` adı ama desensiz, `1920x1080` çözünürlük yanlış eşleşmesi) örnekleri; korumalı liste
reddi ve SystemArtifact istisnası; bulut/reparse dışlaması; puanlama sırası — 200 GB eski
imaj 2 GB hiç oynanmamış oyunu boyut oranının (>50x) beşte birinden daha az bir puan
oranıyla geçiyor (log ölçekleme doğrulandı); "bilinmiyor" kullanım sinyali `Score > 0`
üretiyor, sıfır sayılmıyor; tarayıcı önbellek birimleri yalnız beyaz listeli alt klasörleri
çözdüğü için History/Cookies yapısal olarak asla dahil olmuyor.

Derleme (`dotnet build src/DustyBytes.Units`): "Oluşturma başarılı oldu. 0 Uyarı 0 Hata".

## Ölçümler

- 9 çıkarıcı, 1 birleştirici (`UnitBuilder`), 1 salt-okunur liste (`LargeFileList`).
- 35 test, tamamı yeşil.
- `rules/dev-artifacts.json`: 23 ekosistem girdisi.

## Yapılamayanlar

Yok — beşin görev listesindeki tüm maddeler tamamlandı. `rules/units.json` ve
`tests/fixtures/units/` bilinçli olarak atlandı, gerekçesi yukarıda.

## Notlar

- Derleme sırasında `src/DustyBytes.Scan` içinde başka bir ajanın eşzamanlı çalışmasından
  kaynaklanan geçici CS4004 hataları görüldü (kendi dosyalarım değil, dokunulmadı); birkaç
  deneme sonrası temiz derleme alındı.
- `UnitBuilder.cs`'de `Core.Paths.*` çağrıları `using DustyBytes.Core;` eksikliğinden
  derlenmiyordu; import eklenip çağrılar `Paths.*` olarak düzeltildi.
- `SeriesExtractor` için `NxN` regex'inde nokta içeren negatif lookbehind
  (`OldShow.1x01.avi` gibi adları reddediyordu) düzeltildi: `(?<!\d)(\d{1,2})x(\d{2,3})(?!\d)`.
