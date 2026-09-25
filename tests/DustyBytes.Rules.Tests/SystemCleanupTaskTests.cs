using DustyBytes.Clean.SystemCleanup;

namespace DustyBytes.Rules.Tests;

public class SystemCleanupTaskTests
{
    [Fact]
    public void DiskCleanupBuildsSageArgumentsWithConsistentNumber()
    {
        Assert.Equal($"/sageset:{DiskCleanup.SageNumber}", DiskCleanup.BuildSagesetArguments());
        Assert.Equal($"/sagerun:{DiskCleanup.SageNumber}", DiskCleanup.BuildSagerunArguments());
    }

    [Fact]
    public void DiskCleanupTargetsPreviousInstallationsAndSetupFiles()
    {
        Assert.Contains("Previous Installations", DiskCleanup.Handlers);
        Assert.Contains("Temporary Setup Files", DiskCleanup.Handlers);
    }

    [Fact]
    public void WindowsUpdateCacheTargetsBothServices()
    {
        Assert.Contains("wuauserv", WindowsUpdateCache.Services);
        Assert.Contains("bits", WindowsUpdateCache.Services);
    }

    [Fact]
    public void WindowsUpdateCacheDownloadDirIsUnderSoftwareDistribution()
    {
        Assert.Contains("SoftwareDistribution", WindowsUpdateCache.DownloadDir);
        Assert.Contains("Download", WindowsUpdateCache.DownloadDir);
    }

    [Fact]
    public void DeliveryOptimizationUsesServiceCmdletNotManualDelete()
    {
        Assert.Contains("Delete-DeliveryOptimizationCache", DeliveryOptimizationCleanup.PowerShellCommand);
    }
}
