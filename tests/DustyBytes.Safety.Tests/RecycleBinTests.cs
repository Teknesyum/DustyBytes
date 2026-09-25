using DustyBytes.Clean.Safety;

namespace DustyBytes.Safety.Tests;

public class RecycleBinTests
{
    [Fact]
    public void Kutuya_Gonder_Bul_Geri_Yukle_Ve_Temizle()
    {
        using var tree = new TempTree();
        var name = $"dustybytes-{Guid.NewGuid():N}.txt";
        var file = tree.File(Path.Combine("kutu", name), "geri dönüşüm testi");
        var bin = new RecycleBin(SafetyGate.LoadDefault());

        var sent = bin.Send(file);
        Assert.True(sent.Status == OpStatus.Done, sent.Message);
        Assert.False(File.Exists(file));

        var found = bin.FindByOriginalPath(file);
        var item = Assert.Single(found);
        Assert.Equal(Path.GetDirectoryName(file), item.OriginalLocation, ignoreCase: true);
        Assert.NotNull(item.DeletedUtc);
        Assert.True(DateTime.UtcNow - item.DeletedUtc!.Value < TimeSpan.FromMinutes(5));

        Directory.Delete(Path.GetDirectoryName(file)!);
        var restored = bin.Restore(item);
        Assert.True(restored.Status == OpStatus.Done, restored.Message);
        Assert.Contains("yeniden kuruldu", restored.Message);
        Assert.Equal("geri dönüşüm testi", File.ReadAllText(file));
        Assert.Empty(bin.FindByOriginalPath(file));

        var again = bin.Send(file);
        Assert.True(again.Ok, again.Message);
        File.WriteAllText(file, "çakışan");
        var second = Assert.Single(bin.FindByOriginalPath(file));
        var conflict = bin.Restore(second);
        Assert.Equal(OpStatus.Conflict, conflict.Status);
        Assert.Equal("çakışan", File.ReadAllText(file));

        var purged = bin.Purge(second);
        Assert.True(purged.Status == OpStatus.Done, purged.Message);
        Assert.Empty(bin.FindByOriginalPath(file));
    }

    [Fact]
    public void Korumali_Yol_Kutuya_Gonderilmez()
    {
        var bin = new RecycleBin(SafetyGate.LoadDefault());
        var result = bin.Send(Environment.ExpandEnvironmentVariables(@"%WINDIR%\notepad.exe"));
        Assert.Equal(OpStatus.Denied, result.Status);
    }
}
