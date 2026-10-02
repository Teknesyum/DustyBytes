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
    public async Task DiskCleanupReturnsMeasuredFreeSpaceDifference()
    {
        var readings = new Queue<long>([100, 350]);
        var task = new DiskCleanup(
            freeSpace: _ => readings.Dequeue(),
            runner: (_, _, _, _) => Task.FromResult((0, "")),
            windowsDir: @"C:\Windows");

        var result = await task.RunAsync(new Progress<string>(), CancellationToken.None, (_, _) => { });

        Assert.True(result.Ok);
        Assert.Equal(250, result.FreedBytes);
    }

    [Fact]
    public async Task DiskCleanupReportsZeroWhenSpaceShrinksOrCannotBeMeasured()
    {
        var shrink = new Queue<long>([500, 200]);
        var shrinking = new DiskCleanup(_ => shrink.Dequeue(), (_, _, _, _) => Task.FromResult((0, "")), @"C:\Windows");
        Assert.Equal(0, (await shrinking.RunAsync(new Progress<string>(), CancellationToken.None, (_, _) => { })).FreedBytes);

        var broken = new DiskCleanup(_ => throw new IOException("yok"), (_, _, _, _) => Task.FromResult((2, "")), @"C:\Windows");
        var result = await broken.RunAsync(new Progress<string>(), CancellationToken.None, (_, _) => { });
        Assert.False(result.Ok);
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public async Task DiskCleanupEstimateLeavesOutSetupLogsItCannotFree()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-dc-" + Guid.NewGuid().ToString("N"));
        var windows = Path.Combine(root, "Windows");
        Directory.CreateDirectory(Path.Combine(windows, "Panther"));
        File.WriteAllBytes(Path.Combine(windows, "Panther", "setup.log"), new byte[4096]);
        try
        {
            var task = new DiskCleanup(windowsDir: windows);
            var empty = await task.EstimateAsync(CancellationToken.None);
            Assert.Equal(0, empty.RecoverableBytes);
            Assert.False(empty.Recommended);

            Directory.CreateDirectory(Path.Combine(root, "Windows.old"));
            File.WriteAllBytes(Path.Combine(root, "Windows.old", "kernel.bin"), new byte[8192]);
            var full = await task.EstimateAsync(CancellationToken.None);
            Assert.True(full.RecoverableBytes >= 8192);
            Assert.True(full.Recommended);
            Assert.DoesNotContain("Panther", full.Detail);
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
