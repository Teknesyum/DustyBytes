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

    public static IReadOnlyList<ISystemCleanupTask> SystemTasks(ProtectedList protection, string? userSid = null)
    {
        var gate = new SafetyGate(protection);
        return
        [
            new WindowsUpdateCache(new WindowsSystemService(), protection),
            new DeliveryOptimizationCleanup(),
            new DiskCleanup(),
            new ComponentStoreCleanup(),
            new RecycleBinCleanup(gate, userSid, old: true),
            new RecycleBinCleanup(gate, userSid, old: false),
            new HibernationTask(gate),
        ];
    }

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

    public static void Register(WorkerServices services, int? parentPid = null)
    {
        var protection = services.Gate.List;
        var shortcutStore = ShortcutStore();

        Task<bool> QuarantineLeftover(string path) =>
            Task.FromResult(IsPlacedShortcut(path)
                ? shortcutStore.Quarantine(path, includeUserData: true).Ok
                : services.Quarantine.Quarantine(path).Ok);

        var user = parentPid is { } pid
            ? UserScope.Resolve(WorkerNative.ProcessUser(pid)?.Value, WindowsRegistryView.Instance)
            : UserScope.Fallback("Arayüz süreci bilinmiyor");
        var uninstall = new UninstallHandlers(protection, QuarantineLeftover) { User = user };
        WorkerHandlers.Register(Ops.Uninstall, uninstall.HandleUninstall);
        WorkerHandlers.Register(Ops.RemoveLeftovers, uninstall.HandleRemoveLeftovers);
        WorkerHandlers.Register(Ops.ForceUninstall, uninstall.HandleForceUninstall);

        WorkerHandlers.Register(Ops.Clean, (req, progress, ct) =>
            Task.Run(() => WorkerCleanHandlers.HandleClean(req, LoadCatalog(protection), new RealFileDeleter(), progress, ct), ct));

        WorkerHandlers.Register(Ops.CleanPreview, (req, progress, ct) =>
            Task.Run(() => WorkerCleanHandlers.HandleCleanPreview(req, LoadCatalog(protection), ct), ct));

        WorkerHandlers.Register(Ops.SystemClean, (req, progress, ct) =>
            WorkerCleanHandlers.HandleSystemClean(req, SystemTasks(protection, user.Sid), progress, ct));

        var space = new SpaceHandlers(services.Gate);
        WorkerHandlers.Register(Ops.Compress, space.HandleCompress);
        WorkerHandlers.Register(Ops.Uncompress, space.HandleUncompress);
        WorkerHandlers.Register(Ops.CloudFree, space.HandleCloudFree);
        WorkerHandlers.Register(Ops.CloudKeep, space.HandleCloudKeep);

        WorkerHandlers.Register(Ops.FastScan, FastScan);
        WorkerHandlers.Register(Ops.UsnRefresh, UsnRefresh);
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
        var path = ScanTreeCodec.NewPath(Paths.AppData);
        ScanTreeCodec.Write(result, path);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            Payload = path,
            Message = $"Hızlı tarama bitti: {result.Files:N0} dosya, {result.Directories:N0} klasör",
        };
    }

    public static async Task<WorkerResponse> UsnRefresh(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        WorkerResponse Fail(string message) => new() { Id = request.Id, Ok = false, Message = message };
        if (request.Items is not [var input] || !ScanTreeCodec.IsTreePath(input, Paths.AppData) || !File.Exists(input))
            return Fail("Yenilenecek tarama dosyası geçersiz");
        progress.Report(new WorkerProgress(request.Id, "Değişiklikler okunuyor", -1, "Son taramadan bu yana değişen dosyalar, yönetici izniyle"));
        ScanResult cached;
        try
        {
            cached = ScanTreeCodec.Read(input);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Fail("Tarama dosyası okunamadı: " + e.Message);
        }
        if (cached.Usn is null)
            return Fail("Taramada USN konumu yok");
        UsnUpdateResult update;
        try
        {
            update = await UsnUpdater.ApplyAsync(cached, ct, privileged: true).ConfigureAwait(false);
        }
        catch (Exception e) when (e is UsnJournalResetException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return Fail(e.Message);
        }
        var path = ScanTreeCodec.NewPath(Paths.AppData);
        ScanTreeCodec.Write(update.Result, path);
        return new WorkerResponse
        {
            Id = request.Id,
            Ok = true,
            Payload = path,
            Message = $"Yenileme bitti: {update.Changes:N0} değişiklik",
        };
    }

    sealed class Relay(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
