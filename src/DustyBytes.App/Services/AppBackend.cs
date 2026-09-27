using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;
using DustyBytes.Units;
using DustyBytes.Worker;
using ScanProgress = DustyBytes.Scan.ScanProgress;
using LeftoverProgress = DustyBytes.Clean.Uninstall.ScanProgress;

namespace DustyBytes.App.Services;

public sealed class AppBackend : IAppBackend, IAsyncDisposable
{
    readonly SemaphoreSlim _workerGate = new(1, 1);
    readonly Ledger _ledger = new();
    readonly Lazy<Task<UsageIndex>> _usage;
    WorkerClient? _worker;
    IReadOnlyList<InstalledProgram>? _programs;

    public AppBackend()
    {
        ScanRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        _usage = new(() => Task.Run(() => UsageIndex.Collect()));
    }

    public string ScanRoot { get; }
    public bool DryRun => Core.DryRun.Enabled;
    public bool WorkerRunning => _worker is { IsConnected: true };
    public bool Winapp2Present => File.Exists(WorkerBindings.Winapp2Path);

    ProtectedList Protection(UsageIndex? usage)
    {
        var list = ProtectedList.LoadDefault();
        try
        {
            list.LoadUserExceptions(Path.Combine(Paths.AppData, "exceptions.json"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
        if (usage is null)
            return list;
        foreach (var root in usage.LibraryRoots)
        {
            var launcher = usage.Games.FirstOrDefault(g => SafeUnder(g.InstallDir, root))?.Launcher ?? "Oyun";
            list.AddLauncherLibrary(root, launcher);
        }
        return list;
    }

    static bool SafeUnder(string path, string root)
    {
        try
        {
            return Paths.IsUnder(path, root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static IProgress<TaskStep>? Span(IProgress<TaskStep>? progress, double from, double to) =>
        progress is null ? null : new Relay<TaskStep>(p => progress.Report(p with { Percent = p.Percent is >= 0 and <= 100 ? from + (to - from) * p.Percent / 100 : -1 }));

    static Action<long, long>? Rows(IProgress<TaskStep>? progress, string step, double from, double to) =>
        progress is null ? null : (read, total) => progress.Report(new TaskStep(step, from + (to - from) * Math.Min(1, read / (double)Math.Max(1, total)), $"{Format.Count(read)} / {Format.Count(total)} kayıt"));

    async Task<ScanSnapshot> Build(ScanResult result, IProgress<TaskStep>? progress, CancellationToken ct, double from = 90)
    {
        progress?.Report(new TaskStep("Kullanım izleri okunuyor", from, "Steam, Epic ve Windows kayıtları"));
        var usage = await _usage.Value.WaitAsync(ct).ConfigureAwait(false);
        var start = from + (100 - from) / 4;
        progress?.Report(new TaskStep("Birimler toplanıyor", start, $"{Format.Count(result.Files)} dosya gruplanıyor"));
        var units = await Task.Run(() =>
        {
            var protection = Protection(usage);
            return UnitBuilder.Build(new UnitContext
            {
                ScanResult = result,
                UsageIndex = usage,
                Protected = protection,
                Now = DateTimeOffset.Now,
                Programs = ProgramsForUnits(protection),
            }, (done, total) => progress?.Report(new TaskStep("Birimler toplanıyor", start + (99 - start) * done / total, $"{done} / {total} tür tarandı")));
        }, ct).ConfigureAwait(false);
        return new ScanSnapshot(result, units, result.FinishedAt, result.Method);
    }

    IReadOnlyList<ProgramInstall> ProgramsForUnits(ProtectedList protection)
    {
        IReadOnlyList<InstalledProgram> programs;
        try
        {
            programs = _programs ?? InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = false });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            return [];
        }
        return UnitPrograms.From(programs, protection);
    }

    public async Task<ScanSnapshot?> LoadCachedAsync(IProgress<TaskStep>? progress, CancellationToken ct)
    {
        var rows = Rows(progress, "Önceki tarama okunuyor", 0, 80);
        _ = _usage.Value;
        var result = await Task.Run(() =>
        {
            try
            {
                return new ScanIndex().Load(ScanRoot, rows);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
        return result is null ? null : await Build(result, progress, ct, 80).ConfigureAwait(false);
    }

    public async Task<ScanSnapshot> ScanAsync(IProgress<TaskStep> progress, CancellationToken ct)
    {
        var scan = Span(progress, 0, 85)!;
        var sink = new Relay<ScanProgress>(p => scan.Report(new TaskStep(p.Step, p.Percent, p.CurrentPath)));
        var result = await new FileScanner().ScanAsync(ScanRoot, new ScanOptions(), sink, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        progress.Report(new TaskStep("Sonuç kaydediliyor", 85, "Bir sonraki açılışta hemen gösterilsin diye"));
        await Task.Run(() =>
        {
            try
            {
                new ScanIndex().Save(result);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
            }
        }, ct).ConfigureAwait(false);
        return await Build(result, progress, ct).ConfigureAwait(false);
    }

    public Availability FastScanAvailability()
    {
        if (Environment.ProcessPath is null)
            return new(false, "Yönetici yardımcısı başlatılamıyor: uygulama yolu okunamadı");
        if (!FastScanner.IsSupported(ScanRoot))
            return new(false, $"{ScanRoot} sürücüsü NTFS değil; hızlı tarama yalnız NTFS'te çalışır");
        return new(true, "Yönetici izniyle MFT'den okur; bir kez izin ister");
    }

    public async Task<ScanSnapshot> FastScanAsync(IProgress<TaskStep> progress, CancellationToken ct)
    {
        var response = await SendAsync(new WorkerRequest { Op = Ops.FastScan, Target = ScanRoot }, Span(progress, 0, 75)!, ct).ConfigureAwait(false);
        if (!response.Ok)
            throw new InvalidOperationException(response.Message);
        var path = response.Payload is { Length: > 0 } p && File.Exists(p) ? p : null;
        var rows = Rows(progress, "Hızlı tarama sonucu okunuyor", 75, 90);
        var result = await Task.Run(() => new ScanIndex(path).Load(ScanRoot, rows), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Hızlı tarama sonucu dizinde bulunamadı");
        return await Build(result, progress, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProgramInfo>> ListProgramsAsync(IProgress<TaskStep> progress, CancellationToken ct)
    {
        progress.Report(new TaskStep("Kurulu programlar okunuyor", -1, "Kayıt defteri ve Microsoft Store paketleri"));
        var programs = await Task.Run(() => InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = true }), ct).ConfigureAwait(false);
        _programs = programs;
        progress.Report(new TaskStep("Son kullanım izleri eşleniyor", -1, $"{Format.Count(programs.Count)} program"));
        var usage = await _usage.Value.WaitAsync(ct).ConfigureAwait(false);
        return [.. programs
            .Where(p => !p.IsFramework)
            .Select(p => new ProgramInfo(p, p.InstallLocation is { Length: > 0 } dir ? usage.ForFolder(dir) : UsageSignal.Unknown))
            .OrderByDescending(p => Math.Max(p.Program.SizeBytes, p.Program.EstimatedSizeBytes))];
    }

    public async Task<LeftoverSnapshot> PreviewLeftoversAsync(InstalledProgram program, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var programs = _programs ?? await Task.Run(() => InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = false }), ct).ConfigureAwait(false);
        var sink = new Relay<LeftoverProgress>(p => progress.Report(new TaskStep(p.Step, p.Percent, p.Line)));
        return await Task.Run(() => new LeftoverScanner(ScanContext.ForSystem(Protection(null), programs)).Snapshot(program, sink, ct), ct).ConfigureAwait(false);
    }

    CleanerCatalog? _catalog;

    CleanerCatalog Catalog() => _catalog ??= WorkerBindings.LoadCatalog(Protection(null));

    public Task<IReadOnlyList<CleanRuleInfo>> CleanRulesAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<CleanRuleInfo>>(() =>
        {
            var catalog = Catalog();
            return [.. catalog.Rules.Select(r =>
            {
                var (running, reason) = catalog.IsRunning(r);
                return new CleanRuleInfo(r, running, reason);
            })];
        }, ct);

    public Task<IReadOnlyList<OptionPreview>> PreviewCleanAsync(IReadOnlyList<RuleSelection> selection, CancellationToken ct) =>
        Task.Run(() => Catalog().Preview(selection), ct);

    public async Task<IReadOnlyList<SystemTaskInfo>> SystemTasksAsync(CancellationToken ct)
    {
        var list = new List<SystemTaskInfo>();
        foreach (var task in WorkerBindings.SystemTasks(Protection(null)))
        {
            if (task.Id == "component-store")
            {
                list.Add(new SystemTaskInfo(task.Id, task.Name, 0, false, "", false, "Ölçümü DISM çalıştırır; arayüz bu görevi başlatmaz"));
                continue;
            }
            try
            {
                var estimate = await Task.Run(() => task.EstimateAsync(ct), ct).ConfigureAwait(false);
                list.Add(new SystemTaskInfo(task.Id, task.Name, estimate.RecoverableBytes, estimate.Recommended, estimate.Detail, true, null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                list.Add(new SystemTaskInfo(task.Id, task.Name, 0, false, "", true, "Boyut ölçülemedi: " + e.Message));
            }
        }
        return list;
    }

    public async Task<QuarantineSnapshot> ReadQuarantineAsync(CancellationToken ct)
    {
        if (WorkerRunning)
        {
            var response = await SendAsync(new WorkerRequest { Op = Ops.ListQuarantine }, new Progress<TaskStep>(), ct).ConfigureAwait(false);
            if (response.Ok && response.Payload is { Length: > 0 } json)
            {
                var listing = System.Text.Json.JsonSerializer.Deserialize(json, WorkerJson.Default.QuarantineListing);
                if (listing is not null)
                    return new QuarantineSnapshot(listing.Entries, listing.Usage, true, null);
            }
        }
        return await Task.Run(QuarantineReader.Read, ct).ConfigureAwait(false);
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request, IProgress<TaskStep> progress, CancellationToken ct)
    {
        await _workerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_worker is not { IsConnected: true })
            {
                if (_worker is not null)
                    await _worker.DisposeAsync().ConfigureAwait(false);
                _worker = null;
                progress.Report(new TaskStep("Yönetici izni bekleniyor", -1, "Windows izin penceresini onaylayın"));
                _worker = await WorkerClient.StartAsync(elevated: true, cancellationToken: ct).ConfigureAwait(false);
            }
            var sink = new Relay<WorkerProgress>(p => progress.Report(new TaskStep(p.Step, p.Percent, p.Line)));
            try
            {
                return await _worker.SendAsync(request, sink, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await _worker.DisposeAsync().ConfigureAwait(false);
                _worker = null;
                throw;
            }
            catch (IOException)
            {
                await _worker.DisposeAsync().ConfigureAwait(false);
                _worker = null;
                throw;
            }
        }
        finally
        {
            _workerGate.Release();
        }
    }

    public LedgerData ReadLedger() => _ledger.Read();

    public LedgerData AddFreed(long bytes) => _ledger.Add(bytes);

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
            await _worker.DisposeAsync().ConfigureAwait(false);
        _worker = null;
    }

    sealed class Relay<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
