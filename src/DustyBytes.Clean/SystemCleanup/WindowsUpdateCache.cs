using DustyBytes.Clean.Rules;
using DustyBytes.Core;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class WindowsUpdateCache(ISystemService services, ProtectedList protectedList) : ISystemCleanupTask
{
    public static readonly string[] Services = ["wuauserv", "bits"];
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    public string Id => "windows-update-cache";
    public string Name => "Windows Update İndirme Önbelleği";

    public static string DownloadDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

    public Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var bytes = FolderSize.Measure(DownloadDir);
        return Task.FromResult(new SystemCleanupEstimate(Id, bytes, bytes > 0, DownloadDir));
    }

    public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct) =>
        RunAsync(progress, ct, new RealDeleter());

    public Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct, ICleanupDeleter deleter)
    {
        var dir = DownloadDir;
        if (!Directory.Exists(dir))
            return Task.FromResult(new SystemCleanupResult(Id, true, "Önbellek klasörü yok", 0));

        var wasRunning = Services.ToDictionary(s => s, s => SafeIsRunning(s));

        if (DryRun.Enabled)
        {
            var bytes = FolderSize.Measure(dir);
            progress.Report($"[prova] {dir} içeriği silinecekti ({Format.Bytes(bytes)})");
            return Task.FromResult(new SystemCleanupResult(Id, true, "Prova kipi: silme yapılmadı", 0));
        }

        foreach (var svc in Services)
        {
            progress.Report($"{svc} durduruluyor");
            services.Stop(svc, ServiceTimeout);
        }

        long freed = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (!protectedList.Check(file).Allowed)
                    continue;
                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    continue;
                }
                if (deleter.DeleteFile(file))
                {
                    freed += size;
                    progress.Report($"Silindi: {file}");
                }
            }
        }
        finally
        {
            foreach (var svc in Services)
            {
                if (wasRunning[svc])
                {
                    progress.Report($"{svc} yeniden başlatılıyor");
                    services.Start(svc, ServiceTimeout);
                }
            }
        }

        return Task.FromResult(new SystemCleanupResult(Id, true, "Windows Update önbelleği temizlendi", freed));
    }

    bool SafeIsRunning(string service)
    {
        try
        {
            return services.IsRunning(service);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    sealed class RealDeleter : ICleanupDeleter
    {
        public bool DeleteFile(string path)
        {
            try
            {
                File.Delete(Paths.ToLong(path));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool DeleteRegistryValue(string keyPath, string? valueName) => false;
    }
}
