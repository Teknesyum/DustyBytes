using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.Safety;
using DustyBytes.Clean.SystemCleanup;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using ScanProgress = DustyBytes.Scan.ScanProgress;

namespace DustyBytes.Worker;

public static class WorkerBindings
{
    public static string Winapp2Path => Path.Combine(Paths.AppData, "winapp2.ini");

    public static CleanerCatalog LoadCatalog(ProtectedList protection)
    {
        var baseCatalog = CleanerCatalog.LoadDefault(protection);
        if (!File.Exists(Winapp2Path))
            return baseCatalog;
        try
        {
            var extra = Winapp2.Parse(File.ReadAllText(Winapp2Path));
            return new CleanerCatalog([.. baseCatalog.Rules, .. extra], protection);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            return baseCatalog;
        }
    }

    public static IReadOnlyList<ISystemCleanupTask> SystemTasks(ProtectedList protection) =>
    [
        new WindowsUpdateCache(new WindowsSystemService(), protection),
        new DeliveryOptimizationCleanup(),
        new DiskCleanup(),
        new ComponentStoreCleanup(),
    ];

    public static IReadOnlyList<string> ShortcutPlaces() =>
        ScanContext.Clean(
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        ]);

    public static bool IsPlacedShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var norm = Paths.Normalize(path);
            return ShortcutPlaces().Any(d => Paths.IsUnder(norm, d) && !norm.Equals(d, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static void Register(WorkerServices services)
    {
        var protection = services.Gate.List;
        var shortcutStore = ShortcutStore();

        Task<bool> QuarantineLeftover(string path) =>
            Task.FromResult(IsPlacedShortcut(path)
                ? shortcutStore.Quarantine(path, includeUserData: true).Ok
                : services.Quarantine.Quarantine(path).Ok);

        var uninstall = new UninstallHandlers(protection, QuarantineLeftover);
        WorkerHandlers.Register(Ops.Uninstall, uninstall.HandleUninstall);
        WorkerHandlers.Register(Ops.RemoveLeftovers, uninstall.HandleRemoveLeftovers);

        WorkerHandlers.Register(Ops.Clean, (req, progress, ct) =>
            Task.Run(() => WorkerCleanHandlers.HandleClean(req, LoadCatalog(protection), new RealFileDeleter(), progress, ct), ct));

        WorkerHandlers.Register(Ops.SystemClean, (req, progress, ct) =>
            WorkerCleanHandlers.HandleSystemClean(req, SystemTasks(protection), progress, ct));

        WorkerHandlers.Register(Ops.FastScan, FastScan);
    }

    static QuarantineStore ShortcutStore()
    {
        var rules = ProtectedRules.LoadDefault();
        rules.Roots = [.. rules.Roots.Where(r => !r.Path.Contains("Start Menu", StringComparison.OrdinalIgnoreCase))];
        var list = new ProtectedList(rules);
        list.LoadUserExceptions(Path.Combine(Paths.AppData, "exceptions.json"));
        var gate = new SafetyGate(list);
        return new QuarantineStore(gate, null, new RecycleBin(gate));
    }

    public static async Task<WorkerResponse> FastScan(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        var root = string.IsNullOrWhiteSpace(request.Target) ? Path.GetPathRoot(Environment.SystemDirectory)! : request.Target;
        if (Path.GetPathRoot(root) is not { } pathRoot || !pathRoot.Equals(root, StringComparison.OrdinalIgnoreCase))
            return new WorkerResponse { Id = request.Id, Ok = false, Message = $"Hızlı tarama yalnız sürücü kökünde çalışır: {root}" };
        if (!FastScanner.IsSupported(root))
            return new WorkerResponse { Id = request.Id, Ok = false, Message = $"Bu sürücü hızlı taramayı desteklemiyor (NTFS değil ya da erişilemiyor): {root}" };

        var sink = new Relay(p => progress.Report(new WorkerProgress(request.Id, p.Step, p.Percent, p.CurrentPath)));
        var result = await new FastScanner().ScanAsync(root, new ScanOptions(), sink, ct).ConfigureAwait(false);
        var index = new ScanIndex();
        index.Save(result);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            Payload = index.Path,
            Message = $"Hızlı tarama bitti: {result.Files:N0} dosya, {result.Directories:N0} klasör",
        };
    }

    sealed class Relay(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
