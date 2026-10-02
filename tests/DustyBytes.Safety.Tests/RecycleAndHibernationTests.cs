using System.Buffers.Binary;
using System.Text;
using DustyBytes.Clean.Safety;
using DustyBytes.Clean.SystemCleanup;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Safety.Tests;

public class RecycleGateTests
{
    public const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    static SafetyGate Gate()
    {
        var gate = SafetyGate.LoadDefault();
        gate.List.AttributeProvider = _ => null;
        return gate;
    }

    static string Bin(string name) => $@"C:\$Recycle.Bin\{Sid}\{name}";

    [Fact]
    public void Kendi_Kutusundaki_Girdi_Izinli()
    {
        var gate = Gate();
        Assert.True(gate.CheckRecycleEntry(Bin("$RABC123.txt"), Sid).Allowed);
        Assert.True(gate.CheckRecycleEntry(Bin("$IABC123.txt"), Sid).Allowed);
        Assert.True(gate.SystemOpAllowed(SafetyGate.RecycleEmptyOp));
    }

    [Fact]
    public void Genel_Kontrol_Kutuyu_Hala_Reddeder()
    {
        var gate = Gate();
        Assert.False(gate.Check(Bin("$RABC123.txt"), true).Allowed);
        Assert.False(gate.List.CheckPath(Bin("$RABC123.txt")).Allowed);
        Assert.False(gate.List.CheckPath(@"D:\$Recycle.Bin\" + Sid + @"\$RABC").Allowed);
    }

    [Theory]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21-9-9-9-500\$RABC.txt")]
    [InlineData(@"C:\$Recycle.Bin")]
    [InlineData(@"C:\$Recycle.Bin\" + Sid)]
    [InlineData(@"C:\$Recycle.Bin\" + Sid + @"\$RABC\icerik.txt")]
    [InlineData(@"C:\$Recycle.Bin\" + Sid + @"\desktop.ini")]
    [InlineData(@"C:\$Recycle.Bin\" + Sid + @"\$R")]
    [InlineData(@"C:\Users\ali\$RABC.txt")]
    [InlineData(@"C:\klasor\$Recycle.Bin\" + Sid + @"\$RABC")]
    [InlineData(@"C:\$Recycle.Bin\" + Sid + @"\..\..\Windows\$RABC")]
    [InlineData(@"$Recycle.Bin\" + Sid + @"\$RABC")]
    [InlineData("")]
    public void Kutu_Disi_Ya_Da_Baskasinin_Girdisi_Reddedilir(string path)
    {
        Assert.False(Gate().CheckRecycleEntry(path, Sid).Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("S-1-5-21-9-9-9-500")]
    public void Kimlik_Uyusmazsa_Reddedilir(string sid)
    {
        Assert.False(Gate().CheckRecycleEntry(Bin("$RABC.txt"), sid).Allowed);
    }

    [Fact]
    public void Via_Olmayan_Kural_Listesi_Bosaltmaya_Izin_Vermez()
    {
        var rules = new ProtectedRules
        {
            Roots = [new PathRule { Path = "C:/$Recycle.Bin", Reason = "Geri Dönüşüm Kutusu" }],
            Segments = [new NameRule { Name = "$Recycle.Bin", Reason = "Geri Dönüşüm Kutusu" }],
        };
        var gate = new SafetyGate(new ProtectedList(rules)) { List = { AttributeProvider = _ => null } };
        Assert.False(gate.SystemOpAllowed(SafetyGate.RecycleEmptyOp));
        Assert.False(gate.CheckRecycleEntry(Bin("$RABC.txt"), Sid).Allowed);
    }

    [Fact]
    public void Via_Baska_Korumayi_Delmez()
    {
        var gate = Gate();
        gate.List.AddUserException($@"C:\$Recycle.Bin\{Sid}");
        Assert.False(gate.CheckRecycleEntry(Bin("$RABC.txt"), Sid).Allowed);
    }

    [Fact]
    public void Baglanti_Noktasi_Kutuda_Da_Reddedilir()
    {
        var gate = SafetyGate.LoadDefault();
        gate.List.AttributeProvider = p => p.EndsWith("$RLINK", StringComparison.OrdinalIgnoreCase) ? FileAttributes.ReparsePoint : null;
        Assert.False(gate.CheckRecycleEntry(Bin("$RLINK"), Sid).Allowed);
    }

    [Fact]
    public void Hazirda_Bekletme_Yolu_Yalniz_Kendi_Dosyasini_Acar()
    {
        var list = ProtectedList.LoadDefault();
        Assert.True(list.SystemOpAllowed(SafetyGate.HibernateOp));
        Assert.False(list.CheckPath(@"C:\hiberfil.sys").Allowed);
        Assert.True(list.CheckPathVia(@"C:\hiberfil.sys", SafetyGate.HibernateOp).Allowed);
        Assert.False(list.CheckPathVia(@"C:\pagefile.sys", SafetyGate.HibernateOp).Allowed);
        Assert.False(list.CheckPathVia(Bin("$RABC"), SafetyGate.HibernateOp).Allowed);
        Assert.False(list.CheckPathVia(@"C:\Windows\hiberfil.sys", SafetyGate.HibernateOp).Allowed);
    }
}

public class RecycleInfoTests
{
    static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    public static byte[] V2(long size, DateTime deletedUtc, string path)
    {
        var text = Encoding.Unicode.GetBytes(path + "\0");
        var data = new byte[28 + text.Length];
        BinaryPrimitives.WriteInt64LittleEndian(data, 2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), path.Length + 1);
        text.CopyTo(data, 28);
        return data;
    }

    static byte[] V1(long size, DateTime deletedUtc, string path)
    {
        var data = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(data, 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        Encoding.Unicode.GetBytes(path).CopyTo(data, 24);
        return data;
    }

    [Fact]
    public void Surum_2_Okunur()
    {
        var info = RecycleInfoFile.Parse(V2(1234, Now.AddDays(-40), @"C:\Users\ali\film.mkv"));
        Assert.NotNull(info);
        Assert.Equal(1234, info.Size);
        Assert.Equal(Now.AddDays(-40), info.DeletedUtc);
        Assert.Equal(@"C:\Users\ali\film.mkv", info.OriginalPath);
    }

    [Fact]
    public void Surum_1_Okunur()
    {
        var info = RecycleInfoFile.Parse(V1(99, Now.AddDays(-3), @"D:\eski.txt"));
        Assert.NotNull(info);
        Assert.Equal(99, info.Size);
        Assert.Equal(@"D:\eski.txt", info.OriginalPath);
    }

    [Fact]
    public void Bozuk_Kayit_Okunmaz()
    {
        Assert.Null(RecycleInfoFile.Parse(new byte[10]));
        var bad = V2(1, Now, "x");
        BinaryPrimitives.WriteInt64LittleEndian(bad, 3);
        Assert.Null(RecycleInfoFile.Parse(bad));
        var tooLong = V2(1, Now, "x");
        BinaryPrimitives.WriteInt32LittleEndian(tooLong.AsSpan(24), 5000);
        Assert.Null(RecycleInfoFile.Parse(tooLong));
        var negative = V2(-5, Now, "x");
        Assert.Null(RecycleInfoFile.Parse(negative));
    }

    static string FakeBin(TempTree tree)
    {
        var bin = tree.Dir("kutu");
        File.WriteAllBytes(Path.Combine(bin, "$IAAA.txt"), V2(100, Now.AddDays(-40), @"C:\eski.txt"));
        File.WriteAllText(Path.Combine(bin, "$RAAA.txt"), new string('a', 100));
        File.WriteAllBytes(Path.Combine(bin, "$IBBB"), V2(300, Now.AddDays(-5), @"C:\yeni"));
        tree.File(@"kutu\$RBBB\ic.txt", new string('b', 300));
        File.WriteAllText(Path.Combine(bin, "desktop.ini"), "x");
        return bin;
    }

    [Fact]
    public void Tarama_Eski_Ve_Yeniyi_Ayirir()
    {
        using var tree = new TempTree();
        var bin = FakeBin(tree);
        var entries = RecycleInfoFile.Scan(bin);
        Assert.Equal(2, entries.Count);
        var old = Assert.Single(RecycleInfoFile.Select(entries, Now, true));
        Assert.Equal(Path.Combine(bin, "$RAAA.txt"), old.DataPath);
        var recent = Assert.Single(RecycleInfoFile.Select(entries, Now, false));
        Assert.Equal(@"C:\yeni", recent.OriginalPath);
    }

    [Fact]
    public async Task Tahmin_Eskiyi_Onerir_Yeniyi_Onermez()
    {
        using var tree = new TempTree();
        var bin = FakeBin(tree);
        var gate = SafetyGate.LoadDefault();
        var old = new RecycleBinCleanup(gate, RecycleGateTests.Sid, true, () => [bin], () => Now, () => 1000);
        var recent = new RecycleBinCleanup(gate, RecycleGateTests.Sid, false, () => [bin], () => Now, () => 1000);

        var o = await old.EstimateAsync(default);
        Assert.Equal(100, o.RecoverableBytes);
        Assert.True(o.Recommended);
        Assert.True(o.Silent);

        var r = await recent.EstimateAsync(default);
        Assert.Equal(900, r.RecoverableBytes);
        Assert.False(r.Recommended);
        Assert.False(r.Silent);

        Assert.True(((ISystemCleanupTask)old).ExplicitOnly);
        Assert.True(((ISystemCleanupTask)recent).ExplicitOnly);
    }

    [Fact]
    public async Task Calistirma_Kutu_Disindaki_Yolu_Silmez()
    {
        using var tree = new TempTree();
        var bin = FakeBin(tree);
        var task = new RecycleBinCleanup(SafetyGate.LoadDefault(), RecycleGateTests.Sid, true, () => [bin], () => Now);
        var result = await task.RunAsync(new Progress<string>(), default);
        Assert.False(result.Ok);
        Assert.Equal(0, result.FreedBytes);
        Assert.True(File.Exists(Path.Combine(bin, "$RAAA.txt")));
        Assert.True(File.Exists(Path.Combine(bin, "$IAAA.txt")));
    }

    [Fact]
    public async Task Kimlik_Yoksa_Calismaz()
    {
        var task = new RecycleBinCleanup(SafetyGate.LoadDefault(), null, true, () => [], () => Now);
        var result = await task.RunAsync(new Progress<string>(), default);
        Assert.False(result.Ok);
    }
}

public class HibernationTaskTests
{
    sealed class FakePower(bool? battery, bool? enabled, long? bytes, bool? reduced = null) : IPowerInfo
    {
        public long? Bytes { get; set; } = bytes;
        public bool? HibernateReduced() => reduced;
        public bool? HasBattery() => battery;
        public bool? HibernateEnabled() => enabled;
        public long? HiberfilBytes() => Bytes;
    }

    sealed class FakeRunner
    {
        public List<string> Calls { get; } = [];
        public Action? After { get; set; }

        public Task<(int ExitCode, string Output)> Run(string file, string args, IProgress<string>? progress, CancellationToken ct)
        {
            Calls.Add(args);
            After?.Invoke();
            return Task.FromResult((0, ""));
        }
    }

    const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public async Task Masaustunde_Onerilir_Ama_Sessiz_Degil()
    {
        var task = new HibernationTask(SafetyGate.LoadDefault(), new FakePower(false, true, 8 * Gb));
        var e = await task.EstimateAsync(default);
        Assert.Equal(8 * Gb, e.RecoverableBytes);
        Assert.True(e.Recommended);
        Assert.Null(e.Warning);
        Assert.False(e.Silent);
        Assert.Contains("Hızlı Başlangıç", e.Detail);
        Assert.Contains("Geri aç", e.Detail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Dizustunde_Uyarili_Ve_Secilmemis(bool? battery)
    {
        var task = new HibernationTask(SafetyGate.LoadDefault(), new FakePower(battery, true, 6 * Gb));
        var e = await task.EstimateAsync(default);
        Assert.False(e.Recommended);
        Assert.Equal(HibernationTask.LaptopWarning, e.Warning);
        Assert.True(e.Available);
    }

    [Fact]
    public async Task Kapaliyken_Geri_Ac_Sunulur()
    {
        var task = new HibernationTask(SafetyGate.LoadDefault(), new FakePower(false, false, null));
        var e = await task.EstimateAsync(default);
        Assert.False(e.Available);
        Assert.Equal(HibernationTask.OnId, e.RestoreId);
        Assert.Equal(0, e.RecoverableBytes);
    }

    [Fact]
    public async Task Kapatma_Ve_Geri_Acma_Powercfg_Ile()
    {
        var power = new FakePower(false, true, 8 * Gb);
        var runner = new FakeRunner { After = () => power.Bytes = 0 };
        ISystemCleanupTask task = new HibernationTask(SafetyGate.LoadDefault(), power, runner.Run);

        var off = await task.RunAsync(new Progress<string>(), default);
        Assert.True(off.Ok);
        Assert.Equal(8 * Gb, off.FreedBytes);

        var on = await task.RunAsync(HibernationTask.OnId, new Progress<string>(), default);
        Assert.True(on.Ok);
        Assert.Equal(["/hibernate off", "/hibernate on"], runner.Calls);
    }

    [Fact]
    public async Task Kucultme_Ve_Tam_Boyuta_Donme_Powercfg_Type_Ile()
    {
        var power = new FakePower(false, true, 8 * Gb);
        var runner = new FakeRunner { After = () => power.Bytes = 4 * Gb };
        ISystemCleanupTask task = new HibernationTask(SafetyGate.LoadDefault(), power, runner.Run);

        var reduced = await task.RunAsync(HibernationTask.ReducedId, new Progress<string>(), default);
        Assert.True(reduced.Ok);
        Assert.Equal(4 * Gb, reduced.FreedBytes);

        var full = await task.RunAsync(HibernationTask.FullId, new Progress<string>(), default);
        Assert.True(full.Ok);
        Assert.Equal(["/h /type reduced", "/h /type full"], runner.Calls);
        Assert.Contains(HibernationTask.ReducedId, task.ExtraIds);
        Assert.Contains(HibernationTask.FullId, task.ExtraIds);
    }

    [Fact]
    public async Task Kucultme_Tam_Boyutta_Sunulur_Kucukken_Geri_Donus_Sunulur()
    {
        var full = await new HibernationTask(SafetyGate.LoadDefault(), new FakePower(false, true, 8 * Gb, false)).EstimateAsync(default);
        Assert.Equal(HibernationTask.ReducedId, full.AltId);
        Assert.Equal("Küçült (Hızlı Başlangıç korunur)", full.AltLabel);
        Assert.Null(full.RestoreId);

        var small = await new HibernationTask(SafetyGate.LoadDefault(), new FakePower(false, true, 4 * Gb, true)).EstimateAsync(default);
        Assert.Null(small.AltId);
        Assert.Equal(HibernationTask.FullId, small.RestoreId);
        Assert.Equal("Tam boyuta döndür", small.RestoreLabel);
    }

    [Fact]
    public async Task Kural_Listesi_Izin_Vermezse_Kucultme_Powercfg_Cagirmaz()
    {
        var runner = new FakeRunner();
        var gate = new SafetyGate(new ProtectedList(new ProtectedRules
        {
            Files = [new NameRule { Name = "hiberfil.sys", Reason = "Sistem" }],
        }));
        ISystemCleanupTask task = new HibernationTask(gate, new FakePower(false, true, Gb), runner.Run);
        var result = await task.RunAsync(HibernationTask.ReducedId, new Progress<string>(), default);
        Assert.False(result.Ok);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Kural_Listesi_Izin_Vermezse_Powercfg_Cagrilmaz()
    {
        var runner = new FakeRunner();
        var gate = new SafetyGate(new ProtectedList(new ProtectedRules
        {
            Files = [new NameRule { Name = "hiberfil.sys", Reason = "Sistem" }],
        }));
        var task = new HibernationTask(gate, new FakePower(false, true, Gb), runner.Run);
        var result = await task.RunAsync(new Progress<string>(), default);
        Assert.False(result.Ok);
        Assert.Empty(runner.Calls);
    }

    sealed class PlainTask : ISystemCleanupTask
    {
        public int Runs { get; private set; }
        public string Id => "temp";
        public string Name => "Geçici";
        public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct) => Task.FromResult(new SystemCleanupEstimate(Id, 1, true, ""));

        public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(new SystemCleanupResult(Id, true, "", 1));
        }
    }

    [Fact]
    public async Task Bos_Istek_Acik_Secim_Isteyen_Gorevleri_Calistirmaz()
    {
        var runner = new FakeRunner();
        var plain = new PlainTask();
        var gate = SafetyGate.LoadDefault();
        ISystemCleanupTask[] tasks =
        [
            plain,
            new HibernationTask(gate, new FakePower(false, true, Gb), runner.Run),
            new RecycleBinCleanup(gate, RecycleGateTests.Sid, true, () => []),
        ];

        var all = await WorkerCleanHandlers.HandleSystemClean(new WorkerRequest { Op = Ops.SystemClean, UserApproved = true }, tasks);
        Assert.Equal(["temp"], all.Items.Select(i => i.Path));
        Assert.Empty(runner.Calls);

        var restore = await WorkerCleanHandlers.HandleSystemClean(new WorkerRequest { Op = Ops.SystemClean, UserApproved = true, Items = [HibernationTask.OnId] }, tasks);
        Assert.Equal([HibernationTask.OnId], restore.Items.Select(i => i.Path));
        Assert.Equal(["/hibernate on"], runner.Calls);
        Assert.Equal(1, plain.Runs);
    }
}
