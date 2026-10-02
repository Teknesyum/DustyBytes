using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class BulkBodyTests
{
    static ProgramRow Row(string id, InstallerType type, string uninstall) =>
        new(new ProgramInfo(FakeBackend.Program(id, id, "Pub", 1) with { Installer = type, UninstallString = uninstall }, UsageSignal.Unknown), DateTimeOffset.Now);

    [Fact]
    public void BadgeFollowsInstallerType()
    {
        var inno = Row("a", InstallerType.Inno, @"""C:\A\unins000.exe""");
        var unknown = Row("b", InstallerType.Unknown, @"C:\B\remove.exe");

        Assert.Equal(UninstallPlan.SilentBadge, inno.Badge);
        Assert.Equal(UninstallPlan.VisibleBadge, unknown.Badge);
        Assert.True(inno.HasBadge);
    }

    [Fact]
    public void ConfirmationTextSaysHowManyWindowsOpen()
    {
        var mixed = ProgramsViewModel.BulkBody([
            Row("a", InstallerType.Inno, @"""C:\A\unins000.exe"""),
            Row("b", InstallerType.Nsis, @"""C:\B\uninstall.exe"""),
            Row("c", InstallerType.Unknown, @"C:\C\remove.exe"),
        ]);
        Assert.Contains("2 program sessiz kaldırılır", mixed);
        Assert.Contains("1 programın kendi kaldırıcı penceresi açılır", mixed);
        Assert.Contains("Görünür çalıştır", mixed);

        var allSilent = ProgramsViewModel.BulkBody([Row("a", InstallerType.Inno, @"""C:\A\unins000.exe""")]);
        Assert.Contains("hiçbir pencere açılmaz", allSilent);

        var allWindows = ProgramsViewModel.BulkBody([Row("c", InstallerType.Unknown, @"C:\C\remove.exe")]);
        Assert.Contains("hepsinin kendi kaldırıcı penceresi açılır", allWindows);
        Assert.DoesNotContain("Görünür çalıştır", allWindows);
    }
}
