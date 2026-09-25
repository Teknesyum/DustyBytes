using DustyBytes.Clean.Safety;
using DustyBytes.Core.Protection;

namespace DustyBytes.Safety.Tests;

public class ProtectedListTests
{
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    static ProtectedList Load() => ProtectedList.LoadDefault();

    [Fact]
    public void Junction_Icinden_Silme_Reddedilir()
    {
        using var tree = new TempTree();
        var target = tree.Dir("hedef");
        var secret = tree.File(@"hedef\onemli.txt");
        var link = tree.P("baglanti");
        TempTree.Junction(link, target);
        var list = Load();

        var inside = list.Check(Path.Combine(link, "onemli.txt"));
        Assert.False(inside.Allowed);
        Assert.Equal(Badge.Link, inside.Badge);

        var self = list.Check(link);
        Assert.False(self.Allowed);
        Assert.Equal(Badge.Link, self.Badge);

        Assert.True(list.Check(secret).Allowed);
    }

    [Fact]
    public void Bulut_Yer_Tutucusu_Reddedilir()
    {
        using var tree = new TempTree();
        var file = tree.File(@"OneDriveBenzeri\belge.docx");
        var list = Load();
        list.AttributeProvider = p =>
            string.Equals(p, file, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Archive | RecallOnDataAccess
                : ProtectedList.DefaultAttributes(p);

        var verdict = list.Check(file);
        Assert.False(verdict.Allowed);
        Assert.Equal(Badge.Cloud, verdict.Badge);

        var offline = ProtectedList.CheckFileSystem(file, _ => FileAttributes.Offline);
        Assert.Equal(Badge.Cloud, offline.Badge);

        var parentPlaceholder = ProtectedList.CheckFileSystem(file, p =>
            string.Equals(p, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | RecallOnOpen
                : FileAttributes.Archive);
        Assert.Equal(Badge.Cloud, parentPlaceholder.Badge);

        Assert.True(ProtectedList.CheckFileSystem(file).Allowed);
    }

    [Theory]
    [InlineData(@"%ProgramFiles%\Common Files\Vendor\shared.dll")]
    [InlineData(@"%ProgramFiles(x86)%\Common Files\Vendor\shared.dll")]
    [InlineData(@"D:\Araclar\Microsoft Shared\ink\tipband.dll")]
    [InlineData(@"C:\Program Files\Common Files")]
    public void Paylasilan_Bilesen_Reddedilir(string raw)
    {
        var verdict = Load().Check(Environment.ExpandEnvironmentVariables(raw));
        Assert.False(verdict.Allowed);
        Assert.Equal(Badge.Shared, verdict.Badge);
    }

    [Fact]
    public void Launcher_Kutuphane_Koku_Reddedilir()
    {
        using var tree = new TempTree();
        var library = tree.Dir("SteamLibrary");
        var game = tree.File(@"SteamLibrary\steamapps\common\Oyun\oyun.exe");
        var other = tree.File(@"Baska\dosya.bin");
        var list = Load();
        list.AddLauncherLibrary(library, "Steam");

        var inside = list.Check(game);
        Assert.False(inside.Allowed);
        Assert.Equal(Badge.Launcher, inside.Badge);
        Assert.Contains("Steam", inside.Reason);

        Assert.False(list.Check(library).Allowed);
        Assert.False(list.Check(tree.Root).Allowed);
        Assert.True(list.Check(other).Allowed);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"\\?\C:\")]
    public void Surucu_Koku_Reddedilir(string path)
    {
        var verdict = Load().Check(path);
        Assert.False(verdict.Allowed);
        Assert.Equal(Badge.System, verdict.Badge);
    }

    [Theory]
    [InlineData(@"C:")]
    [InlineData(@"klasor\dosya.txt")]
    [InlineData(@"")]
    public void Tam_Olmayan_Yol_Reddedilir(string path)
    {
        Assert.False(new SafetyGate(Load()).Check(path, includeUserData: true).Allowed);
    }

    [Theory]
    [InlineData(@"%WINDIR%")]
    [InlineData(@"%WINDIR%\System32\drivers\etc\hosts")]
    [InlineData(@"%WINDIR%\WinSxS\x")]
    public void Windows_Reddedilir(string raw)
    {
        var verdict = Load().Check(Environment.ExpandEnvironmentVariables(raw));
        Assert.False(verdict.Allowed);
    }

    [Theory]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"D:\pagefile.sys")]
    [InlineData(@"C:\hiberfil.sys")]
    [InlineData(@"C:\swapfile.sys")]
    public void Sistem_Dosyasi_Reddedilir(string path)
    {
        var verdict = Load().CheckPath(path);
        Assert.False(verdict.Allowed);
        Assert.Contains("ayarlardan", verdict.Reason);
    }

    [Fact]
    public void NeverLeftover_Kullanici_Verisi_Onaysiz_Reddedilir()
    {
        var list = Load();
        var downloads = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Downloads\kurulum.exe");
        var saved = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Saved Games\Oyun\kayit.sav");
        var profile = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%");
        Assert.True(list.IsNeverLeftover(downloads));
        Assert.True(list.IsNeverLeftover(saved));
        Assert.True(list.IsNeverLeftover(profile));

        var gate = new SafetyGate(list);
        var denied = gate.Check(downloads, includeUserData: false);
        Assert.False(denied.Allowed);
        Assert.Equal(Badge.UserData, denied.Badge);
        Assert.False(gate.Check(saved, includeUserData: false).Allowed);

        Assert.True(gate.Check(downloads, includeUserData: true).Allowed);
    }

    [Fact]
    public void Kayitli_Oyun_Klasoru_Kullanici_Verisidir()
    {
        using var tree = new TempTree();
        var saves = tree.Dir(@"My Games\Oyun");
        var file = tree.File(@"My Games\Oyun\save1.dat");
        var gate = new SafetyGate(Load());
        gate.AddUserDataRoot(saves);

        var verdict = gate.Check(file, includeUserData: false);
        Assert.False(verdict.Allowed);
        Assert.Equal(Badge.UserData, verdict.Badge);
        Assert.True(gate.Check(file, includeUserData: true).Allowed);
    }

    [Fact]
    public void Kullanici_Verisi_Onayi_Sistem_Korumasini_Asamaz()
    {
        var gate = new SafetyGate(Load());
        var verdict = gate.Check(Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe"), includeUserData: true);
        Assert.False(verdict.Allowed);
    }
}
