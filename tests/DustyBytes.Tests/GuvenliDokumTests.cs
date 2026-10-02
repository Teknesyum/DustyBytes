using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class GuvenliDokumTests
{
    static readonly DateTime When = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    static CleanRuleInfo Rule(string id, string name, bool running, params CleanerOption[] options) =>
        new(new CleanerRule { Id = id, Name = name, Options = [.. options] }, running, null);

    static PreviewFile File(string path, long bytes, string option) => new(path, bytes, When, option);

    [Fact]
    public void Rule_Items_Take_Counts_From_Preview_Not_Estimate()
    {
        IReadOnlyList<CleanRuleInfo> rules =
        [
            Rule("chrome", "Chrome", false,
                new CleanerOption { Id = "cache", Label = "Önbellek", What = "Sitelerin geçici kopyaları." },
                new CleanerOption { Id = "cookies", Label = "Çerezler", Warning = "Oturumlar kapanır" }),
            Rule("edge", "Edge", true, new CleanerOption { Id = "cache", Label = "Önbellek" }),
        ];
        IReadOnlyList<SystemTaskInfo> tasks =
        [
            new("temp", "Windows geçici dosyaları", 120_000_000, true, "Sistem geçici klasörü", true, null),
            new("dism", "Bileşen deposu", 5_000_000_000, true, "DISM", true, null, Silent: false),
        ];
        var preview = CleanPreview.Of([File(@"C:\a\1.tmp", 400, "chrome/cache"), File(@"C:\a\2.tmp", 600, "chrome/cache")]);
        IReadOnlyList<OptionPreview> estimates = [new("chrome", "cache", 99, 9_999_999)];

        var items = SafeBreakdown.Rules(rules, tasks, preview, estimates);

        Assert.Equal(["chrome/cache", "task:temp"], items.Select(i => i.Key));
        var cache = items[0];
        Assert.Equal("Chrome · Önbellek", cache.Name);
        Assert.Equal("Sitelerin geçici kopyaları.", cache.What);
        Assert.Equal(2, cache.Count);
        Assert.Equal(1_000, cache.Bytes);
        Assert.Equal([@"C:\a\2.tmp", @"C:\a\1.tmp"], cache.Files.Select(f => f.Path));
        Assert.Equal(SafeItemKind.Task, items[1].Kind);
        Assert.Equal("Sistem geçici klasörü", items[1].What);
        Assert.Null(items[1].Count);
    }

    [Fact]
    public void Rule_Items_Fall_Back_To_Estimate_Without_Preview()
    {
        IReadOnlyList<CleanRuleInfo> rules = [Rule("chrome", "Chrome", false, new CleanerOption { Id = "cache", Label = "Önbellek" })];
        var items = SafeBreakdown.Rules(rules, [], null, [new OptionPreview("Chrome", "Cache", 7, 7_000)]);

        var cache = Assert.Single(items);
        Assert.Equal(7, cache.Count);
        Assert.Equal(7_000, cache.Bytes);
        Assert.Empty(cache.Files);
        Assert.Equal(UnitKindInfo.Explain(UnitKind.Cache).What, cache.What);
    }

    [Fact]
    public void Option_Missing_From_Preview_Counts_As_Empty()
    {
        IReadOnlyList<CleanRuleInfo> rules = [Rule("chrome", "Chrome", false, new CleanerOption { Id = "cache", Label = "Önbellek" })];
        var items = SafeBreakdown.Rules(rules, [], CleanPreview.Of([]), [new OptionPreview("chrome", "cache", 7, 7_000)]);

        Assert.Equal(0, Assert.Single(items).Bytes);
        Assert.Empty(SafeBreakdown.Order(items));
    }

    [Fact]
    public void Head_Maps_To_Its_Own_Item_Largest_First_And_Capped()
    {
        var head = Enumerable.Range(1, 15).Select(i => File($@"C:\a\{i}.tmp", i * 10, "chrome/cache"))
            .Append(File(@"C:\b\big.log", 1_000_000, "firefox/cache"))
            .Append(File(@"C:\a\upper.tmp", 5, "CHROME/CACHE"));
        var preview = CleanPreview.Of(head);

        var files = SafeBreakdown.FilesFor(preview, "chrome/cache");

        Assert.Equal(SafeBreakdown.FileLimit, files.Count);
        Assert.Equal(150, files[0].Bytes);
        Assert.True(files.Zip(files.Skip(1)).All(p => p.First.Bytes >= p.Second.Bytes));
        Assert.DoesNotContain(files, f => f.Path.StartsWith(@"C:\b", StringComparison.Ordinal));
        Assert.Equal(When, files[0].LastWriteUtc);
        Assert.Equal([@"C:\b\big.log"], SafeBreakdown.FilesFor(preview, "firefox/cache").Select(f => f.Path));
    }

    [Fact]
    public void Order_Puts_Largest_First_And_Drops_Empty_Items()
    {
        SafeItem Item(string key, long bytes, int? count = null) => new(key, SafeItemKind.Rule, key, "", count, bytes, []);
        var ordered = SafeBreakdown.Order([Item("a", 10), Item("b", 0), Item("c", 30), Item("d", 0, 4), Item("e", 20)]);

        Assert.Equal(["c", "e", "a", "d"], ordered.Select(i => i.Key));
    }

    [Fact]
    public void Skipped_Items_Leave_Total_And_Scope()
    {
        SafeItem Item(string key, long bytes) => new(key, SafeItemKind.Rule, key, "", null, bytes, []);
        List<SafeItem> items = [Item("chrome/cache", 100), Item("task:temp", 50), Item("unit:u4", 1_000)];
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CHROME/cache", "unit:u4" };

        Assert.Equal(1_150, SafeBreakdown.Total(items, new HashSet<string>()));
        Assert.Equal(50, SafeBreakdown.Total(items, skipped));

        var (options, tasks) = SafeBreakdown.Scope(["chrome/cache", "firefox/cache"], ["temp", "dism"], new HashSet<string> { "chrome/cache", "task:dism" });
        Assert.Equal(["firefox/cache"], options);
        Assert.Equal(["temp"], tasks);
    }

    [Fact]
    public void Silent_Units_Become_One_Group_Per_Kind()
    {
        var items = SafeBreakdown.Units(FakeBackend.Snapshot().Units);

        var group = Assert.Single(items);
        Assert.Equal("group:DevArtifact", group.Key);
        Assert.Equal(SafeItemKind.Group, group.Kind);
        Assert.Equal("Geliştirici derleme klasörleri · 1 proje", group.Name);
        Assert.Equal(UnitKindInfo.Explain(UnitKind.DevArtifact).What, group.What);
        Assert.Equal(3_000_000_000, group.Bytes);
        var unit = Assert.Single(group.Units);
        Assert.Equal("unit:u4", unit.Key);
        Assert.Equal("Kod › node_modules", unit.Name);
        Assert.Equal(@"C:\Kod\node_modules", unit.Target);
    }

    static Unit Dev(string id, string name, string path, long bytes, UnitKind kind = UnitKind.DevArtifact) =>
        new() { Id = id, Kind = kind, Name = name, Paths = [path], SizeBytes = bytes, Removal = RemovalMethod.DirectDelete };

    [Fact]
    public void Groups_Collect_Many_Units_Largest_First_With_Plain_Distinct_Names()
    {
        List<Unit> units =
        [
            Dev("a", "src-tauri (Cargo)", @"C:\Kod\ProcWitness\src-tauri\target", 5_000),
            Dev("b", "src-tauri (Cargo)", @"C:\Kod\Teknesyum\src-tauri\target", 6_000),
            Dev("c", "ProcWitness.App (.NET)", @"C:\Kod\ProcWitness\ProcWitness.App\bin", 2_000),
            Dev("d", "Discord önbelleği (Ahmet)", @"C:\Users\Ahmet\AppData\Roaming\discord\Cache", 900, UnitKind.Cache),
            Dev("e", "Edge önbelleği", @"C:\Users\Ahmet\AppData\Local\Microsoft\Edge\User Data\Default\Cache", 800, UnitKind.BrowserCache),
            new Unit { Id = "f", Kind = UnitKind.DevArtifact, Name = "Kişisel", Paths = [@"C:\x"], SizeBytes = 1, Removal = RemovalMethod.Quarantine },
        ];

        var items = SafeBreakdown.Order(SafeBreakdown.Units(units));

        Assert.Equal(["group:DevArtifact", "group:Cache", "group:BrowserCache"], items.Select(i => i.Key));
        var dev = items[0];
        Assert.Equal("Geliştirici derleme klasörleri · 3 proje", dev.Name);
        Assert.Equal(13_000, dev.Bytes);
        Assert.Equal(["Teknesyum › src-tauri · Cargo", "ProcWitness › src-tauri · Cargo", "ProcWitness › ProcWitness.App · .NET"], dev.Units.Select(u => u.Name));
        Assert.Equal(@"C:\Kod\ProcWitness\src-tauri\target", dev.Units[1].Place);
        Assert.Equal(@"D:\…\ProcWitness\src-tauri\target", SafeBreakdown.Shorten(@"D:\İş\Kod\ProcWitness\src-tauri\target"));
        Assert.Equal("Uygulama önbellekleri · 1 klasör", items[1].Name);
        Assert.Equal("Discord önbelleği · Ahmet", Assert.Single(items[1].Units).Name);
        Assert.Equal("Edge önbelleği", Assert.Single(items[2].Units).Name);
        Assert.Equal("Ad", SafeBreakdown.Plain("Ad"));
    }

    [Fact]
    public void Skipped_Units_Leave_Only_Their_Share_Of_The_Group()
    {
        var group = Assert.Single(SafeBreakdown.Units([Dev("a", "x (Cargo)", @"C:\K\A\x\target", 100), Dev("b", "x (Cargo)", @"C:\K\B\x\target", 40)]));
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "unit:b" };

        Assert.Equal(100, SafeBreakdown.Total([group], skipped));
        Assert.Equal(140, SafeBreakdown.Total([group], new HashSet<string>()));
        Assert.Equal(0, SafeBreakdown.Total([group], new HashSet<string> { "unit:a", "unit:b" }));
    }

    [Fact]
    public void Reveal_Selects_Existing_Paths_And_Refuses_Others()
    {
        var dir = Directory.CreateTempSubdirectory("dokum-").FullName;
        try
        {
            var file = Path.Combine(dir, "a.tmp");
            System.IO.File.WriteAllText(file, "x");
            Assert.Equal($"/select,\"{file}\"", Gezgin.Arguments(file));
            Assert.Equal($"/select,\"{dir}\"", Gezgin.Arguments(dir));
            Assert.Equal($"\"{dir}\"", Gezgin.Arguments(Path.Combine(dir, "yok", "b.tmp")));
            Assert.Null(Gezgin.Arguments("reg:HKCU\\Software\\X"));
            Assert.Null(Gezgin.Arguments("göreli\\yol.tmp"));
            Assert.Null(Gezgin.Arguments(""));
            Assert.False(SafeFileLine.Of(new SafeFile(CleanerCatalog.RegistryPrefix + "HKCU\\X", 0, default)).CanReveal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Ready(Action<FakeBackend>? setup = null)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        setup?.Invoke(backend);
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await vm.Overview.EstimateAsync();
        await Settle();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Overview_Lists_What_Makes_The_Safe_Total()
    {
        var (vm, _) = await Ready();
        var overview = vm.Overview;

        Assert.True(overview.HasBreakdown);
        Assert.Equal(["group:DevArtifact", "task:temp", "chrome/cache"], overview.SafeItems.Select(r => r.Key));
        Assert.Equal(overview.SafeBytes, overview.SafeItems.Sum(r => r.Item.Bytes));
        var cache = overview.SafeItems[2];
        Assert.Equal($"3 dosya · {Format.Bytes(999_999)}", cache.SizeText);
        Assert.Equal(3, cache.Files.Count);
        Assert.False(overview.SafeItems[1].CanExpand);
        Assert.False(overview.HasAllSafeToggle);
    }

    [AvaloniaFact]
    public async Task Long_Breakdown_Shows_Four_Then_All()
    {
        var (vm, _) = await Ready(b => b.Rules =
        [
            Rule("r", "Kural", false, [.. Enumerable.Range(1, 6).Select(i => new CleanerOption { Id = "o" + i, Label = "Seçenek " + i })]),
        ]);
        var overview = vm.Overview;

        Assert.Equal(SafeBreakdown.Shown, overview.SafeItems.Count);
        Assert.True(overview.HasAllSafeToggle);
        Assert.Equal("Tümünü gör (8 kalem)", overview.AllSafeText);
        overview.ToggleAllSafeCommand.Execute(null);
        Assert.Equal(8, overview.SafeItems.Count);
        Assert.Equal("Daha az göster", overview.AllSafeText);
        overview.ToggleAllSafeCommand.Execute(null);
        Assert.Equal(SafeBreakdown.Shown, overview.SafeItems.Count);
    }

    [AvaloniaFact]
    public async Task Skipping_An_Item_Shrinks_The_Total_And_The_Clean()
    {
        var (vm, backend) = await Ready();
        var overview = vm.Overview;
        var before = overview.SafeBytes;
        var unit = overview.SafeItems.Single(r => r.Key == "group:DevArtifact");
        var task = overview.SafeItems.Single(r => r.Key == "task:temp");

        overview.SkipSafeCommand.Execute(unit);
        overview.SkipSafeCommand.Execute(task);

        Assert.True(unit.IsSkipped);
        Assert.Equal("Geri ekle", unit.SkipText);
        Assert.Equal(before - 3_000_000_000 - 120_000_000, overview.SafeBytes);
        Assert.Equal(Format.Bytes(999_999), overview.SafeSizeText);

        await overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        Assert.Equal(["chrome/cache"], Assert.Single(backend.Requests, r => r.Op == Ops.Clean).Items);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.SystemClean);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Delete);
        Assert.Contains("Atladığınız 2 kalem temizlenmedi", vm.Tour.Summary!.Lines);
    }

    [AvaloniaFact]
    public async Task Skipping_Everything_Keeps_The_List_And_Blocks_The_Clean()
    {
        var (vm, _) = await Ready();
        var overview = vm.Overview;
        foreach (var row in overview.SafeItems.ToList())
            overview.SkipSafeCommand.Execute(row);

        Assert.Equal(0, overview.SafeBytes);
        Assert.True(overview.IsAllSkipped);
        Assert.False(overview.IsSafeEmpty);
        Assert.True(overview.HasBreakdown);
        Assert.False(overview.SafeCleanCommand.CanExecute(null));
        Assert.True(overview.HasSafeReason);

        overview.SkipSafeCommand.Execute(overview.SafeItems[0]);
        Assert.Equal(3_000_000_000, overview.SafeBytes);
        Assert.True(overview.SafeCleanCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Expanding_A_Rule_Fetches_Its_Own_Files_And_Reveal_Only_Shows()
    {
        var (vm, backend) = await Ready(b =>
        {
            b.Rules.Add(Rule("firefox", "Firefox", false, new CleanerOption { Id = "cache", Label = "Önbellek" }));
            b.CleanFiles = keys => CleanPreview.Of(keys.OrderByDescending(k => k, StringComparer.Ordinal).SelectMany(k =>
                Enumerable.Range(0, k.StartsWith("firefox", StringComparison.Ordinal) ? 250 : 30)
                    .Select(i => new PreviewFile($@"C:\Önbellek\{k.Replace('/', '\\')}\{i}.tmp", 1_000 + i, When, k))));
        });
        var overview = vm.Overview;
        var cache = overview.SafeItems.Single(r => r.Key == "chrome/cache");
        Assert.Equal(30, cache.Item.Count);
        Assert.Empty(cache.Files);
        Assert.True(cache.NeedsFiles);

        await overview.ExpandSafeCommand.ExecuteAsync(cache);

        Assert.True(cache.IsExpanded);
        Assert.Equal(["chrome/cache"], backend.PreviewCalls[^1]);
        Assert.Equal(SafeBreakdown.FileLimit, cache.Files.Count);
        Assert.Equal(@"C:\Önbellek\chrome\cache\29.tmp", cache.Files[0].Target);
        Assert.Equal("30 dosyadan en büyük 10 tanesi", cache.FilesNote);
        Assert.False(cache.NeedsFiles);

        var launched = new List<string>();
        var old = Gezgin.Launch;
        Gezgin.Launch = a =>
        {
            launched.Add(a);
            return true;
        };
        try
        {
            overview.RevealFileCommand.Execute(new SafeFileLine("x", "", "", Path.GetTempPath(), true));
            overview.RevealFileCommand.Execute(new SafeFileLine("x", "", "", "reg:HKCU", false));
        }
        finally
        {
            Gezgin.Launch = old;
        }
        Assert.Equal([$"/select,\"{Path.GetTempPath()}\""], launched);
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Clean or Ops.Delete or Ops.SystemClean);

        await overview.ExpandSafeCommand.ExecuteAsync(cache);
        Assert.False(cache.IsExpanded);
    }

    static async Task<MainViewModel> Many(int count)
    {
        var (vm, _) = await Ready();
        var snapshot = FakeBackend.Snapshot();
        vm.Session.SetSnapshot(snapshot with
        {
            Units =
            [
                .. snapshot.Units,
                .. Enumerable.Range(1, count).Select(i => Dev("d" + i, "src-tauri (Cargo)", $@"C:\Kod\Proje{i}\src-tauri\target", 1_000_000L * i)),
            ],
        });
        await vm.Overview.EstimateAsync();
        await Settle();
        return vm;
    }

    [AvaloniaFact]
    public async Task Thousands_Of_Silent_Units_Stay_One_Line_And_Open_Twenty_At_A_Time()
    {
        var vm = await Many(2_000);
        var overview = vm.Overview;

        Assert.Equal(["group:DevArtifact", "task:temp", "chrome/cache"], overview.SafeItems.Select(r => r.Key));
        Assert.False(overview.HasAllSafeToggle);
        var group = overview.SafeItems[0];
        Assert.Equal("Geliştirici derleme klasörleri · 2.001 proje", group.Name);
        Assert.Empty(group.Units);

        await overview.ExpandSafeCommand.ExecuteAsync(group);

        Assert.Equal(SafeBreakdown.UnitPage, group.Units.Count);
        Assert.Equal("Kod › node_modules", group.Units[0].Name);
        Assert.Equal("Proje2000 › src-tauri · Cargo", group.Units[1].Name);
        Assert.True(group.HasMoreUnits);
        Assert.Equal("20 tane daha göster (1.981 kaldı)", group.MoreUnitsText);
        overview.MoreUnitsCommand.Execute(group);
        Assert.Equal(40, group.Units.Count);
        Assert.True(group.Units.Zip(group.Units.Skip(1)).All(p => p.First.Unit.Bytes >= p.Second.Unit.Bytes));

        await overview.ExpandSafeCommand.ExecuteAsync(group);
        Assert.False(group.HasMoreUnits);
        await overview.ExpandSafeCommand.ExecuteAsync(group);
        Assert.Equal(40, group.Units.Count);
    }

    [AvaloniaFact]
    public async Task Skipping_One_Unit_Keeps_The_Rest_Of_Its_Group_In_The_Clean()
    {
        var vm = await Many(3);
        var overview = vm.Overview;
        var backend = (FakeBackend)vm.Backend;
        var group = overview.SafeItems.Single(r => r.IsGroup);
        await overview.ExpandSafeCommand.ExecuteAsync(group);
        var before = overview.SafeBytes;
        var biggest = group.Units.Single(u => u.Key == "unit:u4");

        overview.SkipUnitCommand.Execute(biggest);

        Assert.True(biggest.IsSkipped);
        Assert.Equal("Geri ekle", biggest.SkipText);
        Assert.False(group.IsSkipped);
        Assert.True(group.HasSkipNote);
        Assert.Equal("1 proje bu temizlikte atlanır", group.SkippedText);
        Assert.Equal(before - 3_000_000_000, overview.SafeBytes);
        Assert.Equal(Format.Bytes(6_000_000), group.SizeText);

        await overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        var deleted = backend.Requests.Where(r => r.Op == Ops.Delete).Select(r => r.UnitId).ToList();
        Assert.Equal(["d1", "d2", "d3"], deleted.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("u4", deleted);
    }

    [AvaloniaFact]
    public async Task Skipping_A_Group_Skips_Every_Unit_And_Can_Be_Undone()
    {
        var vm = await Many(3);
        var overview = vm.Overview;
        var group = overview.SafeItems.Single(r => r.IsGroup);
        await overview.ExpandSafeCommand.ExecuteAsync(group);
        overview.SkipUnitCommand.Execute(group.Units[0]);

        overview.SkipSafeCommand.Execute(group);

        Assert.True(group.IsSkipped);
        Assert.All(group.Units, u => Assert.True(u.IsSkipped));
        Assert.Equal(["unit:d1", "unit:d2", "unit:d3", "unit:u4"], vm.Session.SafeSkips.Order(StringComparer.Ordinal));
        Assert.Equal("Bu temizlikte atlanır", group.SkippedText);

        overview.SkipSafeCommand.Execute(group);

        Assert.False(group.IsSkipped);
        Assert.Empty(vm.Session.SafeSkips);
        Assert.False(group.HasSkipNote);
    }
}
