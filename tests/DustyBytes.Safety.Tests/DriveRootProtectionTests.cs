using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Protection;

namespace DustyBytes.Safety.Tests;

public class DriveRootProtectionTests
{
    static readonly ProtectedList List = ProtectedList.LoadDefault();

    [Theory]
    [InlineData(@"D:\Windows")]
    [InlineData(@"D:\Windows\System32\drivers")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"E:\$Recycle.Bin\S-1-5-21")]
    [InlineData(@"E:\Recovery")]
    [InlineData(@"F:\WpSystem\S-1-5-21\AppData")]
    [InlineData(@"D:\Program Files\WindowsApps\Paket_1.0")]
    [InlineData(@"D:\Program Files")]
    [InlineData(@"G:\Config.Msi")]
    [InlineData(@"D:\")]
    public void SystemFoldersOnEveryDriveAreDenied(string path)
    {
        Assert.False(List.CheckPath(path).Allowed, path);
        Assert.False(new SafetyGate(List).Check(path, false).Allowed, path);
    }

    [Theory]
    [InlineData(@"D:\Windows.old")]
    [InlineData(@"D:\Games\Windows")]
    [InlineData(@"D:\Filmler\eski.mkv")]
    [InlineData(@"D:\Program Files\Eski Editör")]
    [InlineData(@"E:\Recovery kopyası")]
    public void OrdinaryFoldersOnOtherDrivesStayAllowed(string path)
    {
        Assert.True(List.CheckPath(path).Allowed, path);
    }

    [Fact]
    public void QuarantineFolderOnAnyDriveIsGuarded()
    {
        Assert.False(new SafetyGate(List).Check(@"E:\.dustybytes\quarantine\abc", false).Allowed);
    }

    [Fact]
    public void QuarantineLivesOnTheUnitsOwnVolume()
    {
        var d = new VolumeInfo(@"D:\", @"\\?\Volume{d}\", "NTFS", false, false, true);
        var e = new VolumeInfo(@"E:\", @"\\?\Volume{e}\", "exFAT", false, false, false);

        Assert.Equal(@"D:\.dustybytes\quarantine", QuarantineStore.DefaultRoot(d));
        Assert.Equal(@"E:\.dustybytes\quarantine", QuarantineStore.DefaultRoot(e));
        Assert.True(d.SupportsQuarantine);
        Assert.False(e.SupportsQuarantine);
    }
}
