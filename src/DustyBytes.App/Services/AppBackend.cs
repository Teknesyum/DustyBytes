using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.SpaceSaver;
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
                CloudEligible = CloudEligible,
            }, (done, total) => progress?.Report(new TaskStep("Birimler toplanıyor", start + (99 - start) * done / total, $"{done} / {total} tür tarandı")));
        }, ct).ConfigureAwait(false);
        return new ScanSnapshot(result, units, result.FinishedAt, result.Method);
    }

    static bool CloudEligible(string path)
    {
        try
        {
            var info = new FileInfo(Paths.ToLong(path));
            return info.Exists
                && CloudFiles.IsOldEnough(info.LastWriteTimeUtc, info.LastAccessTimeUtc, DateTime.UtcNow)
                && CloudFiles.IsEligible(CloudFiles.Probe(path), info.Length, info.Attributes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public Task<CompressionEstimate> EstimateCompressionAsync(Unit unit, CancellationToken ct) =>
        Task.Run(() =>
        {
            var files = CompressPlan.Candidates(unit.Paths, Protection(null).CheckGamePath, ct);
            return CompressEstimator.Estimate(files, ct);
        }, ct);

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

    public async Task<ScanSnapshot> ScanAsync(IProgress<TaskStep> progress, CancellationToken ct, IProgress<ScanDraft>? drafts = null)
    {
        var scan = Span(progress, 0, 85)!;
        var sink = new Relay<ScanProgress>(p => scan.Report(new TaskStep(p.Step, p.Percent, p.CurrentPath)));
        var options = new ScanOptions();
        var drafting = drafts is null ? null : await StartDraftsAsync(drafts, ct).ConfigureAwait(false);
        if (drafting is not null)
            options = options with { Priority = drafting.Priority, SubtreeDone = drafting.Done };
        ScanResult result;
        try
        {
            result = await new FileScanner().ScanAsync(ScanRoot, options, sink, ct).ConfigureAwait(false);
        }
        finally
        {
            if (drafting is not null)
                await drafting.StopAsync().ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        var snapshot = await Build(result, progress, ct).ConfigureAwait(false);
        _ = SaveAsync(result);
        return snapshot;
    }

    async Task<ScanDrafts> StartDraftsAsync(IProgress<ScanDraft> drafts, CancellationToken ct)
    {
        var usage = await _usage.Value.WaitAsync(ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var protection = Protection(usage);
            var programs = ProgramsForUnits(protection);
            var context = new UnitContext
            {
                ScanResult = new ScanResult { Root = new ScanNode { Name = ScanRoot, IsDirectory = true } },
                UsageIndex = usage,
                Protected = protection,
                Now = DateTimeOffset.Now,
                Programs = programs,
            };
            return new ScanDrafts(EarlyUnits.For(ScanRoot, usage, programs), context, drafts, TimeSpan.FromMilliseconds(250));
        }, ct).ConfigureAwait(false);
    }

    readonly SemaphoreSlim _saveGate = new(1, 1);

    async Task SaveAsync(ScanResult result)
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => new ScanIndex().Save(result)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
        }
        finally
        {
            _saveGate.Release();
        }
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
        progress.Report(new TaskStep("Hızlı tarama sonucu okunuyor", 75, null));
        var result = await Task.Run(() => ReadTree(response.Payload), ct).ConfigureAwait(false);
        var snapshot = await Build(result, progress, ct).ConfigureAwait(false);
        _ = SaveAsync(result);
        return snapshot;
    }

    static ScanResult ReadTree(string? path)
    {
        if (path is not { Length: > 0 } || !ScanTreeCodec.IsTreePath(path, Paths.AppData) || !File.Exists(path))
            throw new InvalidOperationException("Hızlı tarama sonucu bulunamadı");
        try
        {
            return ScanTreeCodec.Read(path);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            throw new InvalidOperationException("Hızlı tarama sonucu okunamadı: " + e.Message, e);
        }
        finally
        {
            Discard(path);
        }
    }

    static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<ScanSnapshot?> RefreshAsync(ScanResult cached, IProgress<TaskStep> progress, CancellationToken ct)
    {
        if (cached.Usn is null || cached.Cancelled || !string.Equals(cached.Root.Name, ScanRoot, StringComparison.OrdinalIgnoreCase))
            return null;
        progress.Report(new TaskStep("Değişiklikler okunuyor", -1, "Son taramadan bu yana USN günlüğü"));
        var copy = await Task.Run(() => new ScanResult
        {
            Root = ScanTree.Clone(cached.Root),
            Files = cached.Files,
            Directories = cached.Directories,
            Elapsed = cached.Elapsed,
            FinishedAt = cached.FinishedAt,
            Errors = cached.Errors,
            Cancelled = cached.Cancelled,
            Method = cached.Method,
            Usn = cached.Usn,
        }, ct).ConfigureAwait(false);
        ScanResult? result;
        try
        {
            var update = await Task.Run(() => UsnUpdater.ApplyAsync(copy, ct), ct).ConfigureAwait(false);
            result = update.Result;
        }
        catch (UsnJournalResetException)
        {
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            result = WorkerRunning ? await RefreshInWorkerAsync(copy, progress, ct).ConfigureAwait(false) : null;
        }
        if (result is null)
            return null;
        var snapshot = await Build(result, progress, ct).ConfigureAwait(false);
        _ = SaveAsync(result);
        return snapshot;
    }

    async Task<ScanResult?> RefreshInWorkerAsync(ScanResult copy, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var input = ScanTreeCodec.NewPath(Paths.AppData);
        try
        {
            await Task.Run(() => ScanTreeCodec.Write(copy, input), ct).ConfigureAwait(false);
            var response = await SendAsync(new WorkerRequest { Op = Ops.UsnRefresh, Target = ScanRoot, Items = [input] }, Span(progress, 0, 85)!, ct).ConfigureAwait(false);
            return response.Ok ? await Task.Run(() => ReadTree(response.Payload), ct).ConfigureAwait(false) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            Discard(input);
        }
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
