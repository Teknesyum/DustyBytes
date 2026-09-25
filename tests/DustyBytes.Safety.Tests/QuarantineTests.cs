using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Protection;

namespace DustyBytes.Safety.Tests;

public class QuarantineTests
{
    static readonly DateTime Stamp = new(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    static QuarantineStore Store(TempTree tree, TimeSpan? retention = null) =>
        new(SafetyGate.LoadDefault(), new QuarantineOptions
        {
            RootResolver = _ => tree.P(".dustybytes", "quarantine"),
            Retention = retention ?? TimeSpan.FromDays(30),
            FallbackToRecycleBin = false,
        });

    [Fact]
    public void Dosya_Gidis_Donus_Zaman_Ve_Oznitelik_Korunur()
    {
        using var tree = new TempTree();
        var file = tree.File(@"kaynak\rapor.txt", "içerik", Stamp);
        File.SetAttributes(file, FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive);
        var store = Store(tree);

        var moved = store.Quarantine(file, unitId: "unit-1");
        Assert.True(moved.Status == OpStatus.Done, moved.Message);
        Assert.NotNull(moved.Id);
        Assert.False(File.Exists(file));

        var entry = Assert.Single(store.List());
        Assert.Equal(file, entry.OriginalPath, ignoreCase: true);
        Assert.Equal("unit-1", entry.UnitId);
        Assert.Equal(QuarantineState.Pending, entry.State);
        Assert.True(File.Exists(entry.StoredPath));
        Assert.True(entry.ExpiresUtc > DateTime.UtcNow.AddDays(29));

        var root = tree.P(".dustybytes", "quarantine");
        Assert.True((File.GetAttributes(root) & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System));
        Assert.True(File.Exists(Path.Combine(root, "manifest.db")));

        var usage = Assert.Single(store.Usage());
        Assert.Equal(1, usage.Count);
        Assert.Equal(new FileInfo(entry.StoredPath).Length, usage.PendingBytes);
        Assert.True(usage.VolumeBytes > 0);
        Assert.False(usage.Warning);

        var restored = store.Restore(moved.Id!);
        Assert.True(restored.Status == OpStatus.Done, restored.Message);
        Assert.Equal(Stamp, File.GetCreationTimeUtc(file));
        Assert.Equal(Stamp.AddHours(1), File.GetLastWriteTimeUtc(file));
        Assert.Equal(Stamp.AddHours(2), File.GetLastAccessTimeUtc(file));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive, File.GetAttributes(file));
        Assert.Equal("içerik", File.ReadAllText(file));
        Assert.Empty(store.List());
        Assert.Equal(QuarantineState.Restored, Assert.Single(store.List(includeClosed: true)).State);
    }

    [Fact]
    public void Klasor_Gidis_Donus()
    {
        using var tree = new TempTree();
        tree.File(@"proje\node_modules\a\index.js", "a");
        tree.File(@"proje\node_modules\b\c\d.js", "bb");
        var dir = tree.P("proje", "node_modules");
        Directory.SetCreationTimeUtc(dir, Stamp);
        var store = Store(tree);

        var moved = store.Quarantine(dir);
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(3, moved.Bytes);
        Assert.False(Directory.Exists(dir));

        var restored = store.Restore(moved.Id!);
        Assert.True(restored.Ok, restored.Message);
        Assert.Equal("bb", File.ReadAllText(tree.P("proje", "node_modules", "b", "c", "d.js")));
        Assert.Equal(Stamp, Directory.GetCreationTimeUtc(dir));
    }

    [Fact]
    public void Cakisma_Uzerine_Yazmaz()
    {
        using var tree = new TempTree();
        var file = tree.File(@"kaynak\ayar.ini", "eski");
        var store = Store(tree);
        var moved = store.Quarantine(file);
        Assert.True(moved.Ok, moved.Message);

        File.WriteAllText(file, "yeni");
        var restored = store.Restore(moved.Id!);
        Assert.Equal(OpStatus.Conflict, restored.Status);
        Assert.Equal("yeni", File.ReadAllText(file));
        Assert.Equal(QuarantineState.Pending, Assert.Single(store.List()).State);
    }

    [Fact]
    public void Ust_Klasor_Yeniden_Kurulur()
    {
        using var tree = new TempTree();
        var file = tree.File(@"a\b\c\veri.bin", "123");
        var store = Store(tree);
        var moved = store.Quarantine(file);
        Assert.True(moved.Ok, moved.Message);

        Directory.Delete(tree.P("a"), true);
        var restored = store.Restore(moved.Id!);
        Assert.True(restored.Status == OpStatus.Done, restored.Message);
        Assert.Contains("yeniden kuruldu", restored.Message);
        Assert.Equal("123", File.ReadAllText(file));
    }

    [Fact]
    public void Sure_Dolumu_Kalici_Siler()
    {
        using var tree = new TempTree();
        var old = tree.File(@"kaynak\eski.log", "1234");
        var fresh = tree.File(@"kaynak\yeni.log", "5");
        var store = Store(tree);
        var a = store.Quarantine(old);
        var b = store.Quarantine(fresh);
        Assert.True(a.Ok && b.Ok);
        Assert.True(store.SetExpiry(b.Id!, DateTime.UtcNow.AddDays(90)));

        var purged = store.PurgeExpired(DateTime.UtcNow.AddDays(31));
        var result = Assert.Single(purged);
        Assert.Equal(a.Id, result.Id);
        Assert.Equal(OpStatus.Done, result.Status);
        Assert.Equal(4, result.Bytes);

        var pending = Assert.Single(store.List());
        Assert.Equal(b.Id, pending.Id);
        Assert.False(File.Exists(Path.Combine(tree.P(".dustybytes", "quarantine"), a.Id!)));
        Assert.Equal(QuarantineState.Purged, store.List(includeClosed: true).Single(e => e.Id == a.Id).State);

        var explicitPurge = store.Purge(b.Id!);
        Assert.Equal(OpStatus.Done, explicitPurge.Status);
        Assert.Empty(store.List());
        Assert.Equal(OpStatus.NotFound, store.Restore(Guid.NewGuid().ToString("N")).Status);
    }

    [Fact]
    public void Korumali_Ve_Kullanici_Verisi_Reddedilir()
    {
        using var tree = new TempTree();
        var store = Store(tree);
        var downloads = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Downloads\dustybytes-yok.bin");
        var denied = store.Quarantine(downloads);
        Assert.Equal(OpStatus.Denied, denied.Status);
        Assert.Equal(Badge.UserData, denied.Badge);

        tree.File(@"kaynak\x.txt");
        var covering = store.Quarantine(tree.Root);
        Assert.Equal(OpStatus.Denied, covering.Status);

        var windows = store.Quarantine(Environment.ExpandEnvironmentVariables(@"%WINDIR%\win.ini"));
        Assert.Equal(OpStatus.Denied, windows.Status);
    }

    [Fact]
    public void Kilitli_Dosya_Tutan_Sureci_Gosterir()
    {
        using var tree = new TempTree();
        var file = tree.File(@"kaynak\acik.dat", "kilit");
        var store = Store(tree);
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = store.Quarantine(file);
            Assert.Equal(OpStatus.Locked, result.Status);
            Assert.Contains(result.Holders, h => h.Pid == Environment.ProcessId);
            Assert.Empty(store.List());
        }
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Uc_Gunu_Gecen_Oge_Suresi_Uzun_Olsa_Da_Silinir()
    {
        using var tree = new TempTree();
        var file = tree.File(@"kaynak\eski.bin", "123");
        var store = Store(tree);
        var moved = store.Quarantine(file);
        Assert.True(moved.Ok);

        Assert.Empty(store.PurgeExpired(DateTime.UtcNow.AddDays(2), DustyBytes.Core.AppSettings.QuarantineDays));
        var purged = Assert.Single(store.PurgeExpired(DateTime.UtcNow.AddDays(3).AddMinutes(1), DustyBytes.Core.AppSettings.QuarantineDays));
        Assert.Equal(moved.Id, purged.Id);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Varsayilan_Karantina_Suresi_Uc_Gun_Ve_Otomatik_Silme_Acik()
    {
        Assert.Equal(TimeSpan.FromDays(3), new QuarantineOptions().Retention);
        var file = Path.Combine(Path.GetTempPath(), "dustybytes-ayar-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.True(DustyBytes.Core.AppSettings.Load(file).AutoPurge);
        new DustyBytes.Core.AppSettings { AutoPurge = false }.Save(file);
        Assert.False(DustyBytes.Core.AppSettings.Load(file).AutoPurge);
        File.Delete(file);
    }
}
