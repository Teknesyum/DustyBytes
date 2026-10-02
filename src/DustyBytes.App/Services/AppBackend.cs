using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.SpaceSaver;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Scan.Duplicates;
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

    bool? _removable;

    public bool ScanRemovable
    {
        get => _removable ??= AppSettings.Load().ScanRemovable;
        set
        {
            _removable = value;
            try
            {
                (AppSettings.Load() with { ScanRemovable = value }).Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

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

    IReadOnlyList<DriveEntry> ListDrives()
    {
        IReadOnlyList<DriveEntry> list;
        try
        {
            list = DriveCatalog.List(ScanRemovable);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            list = [];
        }
        return list.Count > 0 && list[0].Kind == DriveKind.System ? list : [new DriveEntry(ScanRoot, "", "", DriveKind.System, 0, 0), .. list];
    }

    async Task<ScanSnapshot> Build(IReadOnlyList<ScanResult> results, IReadOnlyList<DriveEntry> drives, IProgress<TaskStep>? progress, CancellationToken ct, double from = 90)
    {
        progress?.Report(new TaskStep("Kullanım izleri okunuyor", from, "Steam, Epic ve Windows kayıtları"));
        var usage = await _usage.Value.WaitAsync(ct).ConfigureAwait(false);
        var start = from + (100 - from) / 4;
        var files = results.Sum(r => r.Files);
        progress?.Report(new TaskStep("Birimler toplanıyor", start, $"{Format.Count(files)} dosya gruplanıyor"));
        var units = await Task.Run(() =>
        {
            var protection = Protection(usage);
            return UnitBuilder.BuildDrives(new UnitContext
            {
                ScanResult = results[0],
                UsageIndex = usage,
                Protected = protection,
                Now = DateTimeOffset.Now,
                Programs = ProgramsForUnits(protection),
                Opened = OldDownloadsExtractor.FileTimes,
                CloudEligible = CloudEligible,
            }, results, (done, total) => progress?.Report(new TaskStep("Birimler toplanıyor", start + (99 - start) * done / total, $"{done} / {total} tür tarandı")));
        }, ct).ConfigureAwait(false);
        var first = results[0];
        return new ScanSnapshot(first, units, results.Min(r => r.FinishedAt), first.Method)
        {
            Drives = results,
            Volumes = [.. drives.Where(d => results.Any(r => SameRoot(r.Root.Name, d.Root)))],
        };
    }

    static bool SameRoot(string a, string b) =>
        a.TrimEnd('\\').Equals(b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

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
        _ = _usage.Value;
        var drives = await Task.Run(ListDrives, ct).ConfigureAwait(false);
        var loaded = await Task.Run<(IReadOnlyList<ScanResult> Found, IReadOnlyList<DriveEntry> Kept)?>(() =>
        {
            var found = new List<ScanResult>();
            var kept = new List<DriveEntry>();
            try
            {
                var index = new ScanIndex();
                for (var i = 0; i < drives.Count; i++)
                {
                    var rows = Rows(progress, "Önceki tarama okunuyor", 80.0 * i / drives.Count, 80.0 * (i + 1) / drives.Count);
                    if (index.Load(drives[i].Root, rows) is { } result)
                    {
                        found.Add(result);
                        kept.Add(drives[i]);
                    }
                    else if (i == 0)
                        return null;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                return null;
            }
            return (found, kept);
        }, ct).ConfigureAwait(false);
        return loaded is not { } l || l.Found.Count == 0 ? null : await Build(l.Found, l.Kept, progress, ct, 80).ConfigureAwait(false);
    }

    public async Task<ScanSnapshot> ScanAsync(IProgress<TaskStep> progress, CancellationToken ct, IProgress<ScanDraft>? drafts = null)
    {
        var drives = await Task.Run(ListDrives, ct).ConfigureAwait(false);
        var tracker = new DriveProgress(Span(progress, 0, 85)!, drives);
        var options = new ScanOptions();
        var drafting = drafts is null ? null : await StartDraftsAsync(drives, drafts, ct).ConfigureAwait(false);
        if (drafting is not null)
            options = options with { Priority = drafting.Priority, SubtreeDone = drafting.Done };
        ScanResult?[] results;
        try
        {
            results = await Task.WhenAll(drives.Select((d, i) => ScanDriveAsync(d, options, tracker.For(i), ct))).ConfigureAwait(false);
        }
        finally
        {
            if (drafting is not null)
                await drafting.StopAsync().ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        return await Finish(drives, results, progress, ct).ConfigureAwait(false);
    }

    async Task<ScanSnapshot> Finish(IReadOnlyList<DriveEntry> drives, IReadOnlyList<ScanResult?> results, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var found = new List<ScanResult>();
        var kept = new List<DriveEntry>();
        for (var i = 0; i < drives.Count; i++)
            if (results[i] is { } r)
            {
                found.Add(r);
                kept.Add(drives[i]);
            }
        if (found.Count == 0 || !SameRoot(found[0].Root.Name, drives[0].Root))
            throw new InvalidOperationException($"{drives[0].Root} sürücüsü taranamadı");
        var snapshot = await Build(found, kept, progress, ct).ConfigureAwait(false);
        _ = SaveAsync(found);
        return snapshot;
    }

    static async Task<ScanResult?> ScanDriveAsync(DriveEntry drive, ScanOptions options, IProgress<ScanProgress> sink, CancellationToken ct)
    {
        try
        {
            return await new FileScanner().ScanAsync(drive.Root, options, sink, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (drive.Kind != DriveKind.System && e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return null;
        }
    }

    async Task<ScanDrafts> StartDraftsAsync(IReadOnlyList<DriveEntry> drives, IProgress<ScanDraft> drafts, CancellationToken ct)
    {
        var usage = await _usage.Value.WaitAsync(ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var protection = Protection(usage);
            var programs = ProgramsForUnits(protection);
            var context = new UnitContext
            {
                ScanResult = new ScanResult { Root = new ScanNode { Name = drives[0].Root, IsDirectory = true } },
                UsageIndex = usage,
                Protected = protection,
                Now = DateTimeOffset.Now,
                Programs = programs,
                Opened = OldDownloadsExtractor.FileTimes,
            };
            var early = drives.Select(d => EarlyUnits.For(d.Root, usage, programs)).ToList();
            return new ScanDrafts([.. drives.Select(d => d.Root)], early, context, drafts, TimeSpan.FromMilliseconds(250));
        }, ct).ConfigureAwait(false);
    }

    readonly SemaphoreSlim _saveGate = new(1, 1);

    async Task SaveAsync(IReadOnlyList<ScanResult> results)
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var index = new ScanIndex();
                foreach (var result in results)
                    index.Save(result);
            }).ConfigureAwait(false);
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
        return new(true, "Yönetici izniyle NTFS sürücülerini MFT'den okur; bir kez izin ister");
    }

    public async Task<ScanSnapshot> FastScanAsync(IProgress<TaskStep> progress, CancellationToken ct)
    {
        var drives = await Task.Run(ListDrives, ct).ConfigureAwait(false);
        var tracker = new DriveProgress(Span(progress, 0, 85)!, drives);
        var ntfs = drives.Select(d => d.Kind == DriveKind.System || d.Format.Equals("NTFS", StringComparison.OrdinalIgnoreCase)).ToArray();
        var local = drives.Select((d, i) => ntfs[i] ? Task.FromResult<ScanResult?>(null) : ScanDriveAsync(d, new ScanOptions(), tracker.For(i), ct)).ToArray();
        var results = new ScanResult?[drives.Count];
        for (var i = 0; i < drives.Count; i++)
        {
            if (!ntfs[i])
                continue;
            try
            {
                results[i] = await FastScanDriveAsync(drives[i], tracker.Steps(i), ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (i > 0)
            {
                results[i] = await ScanDriveAsync(drives[i], new ScanOptions(), tracker.For(i), ct).ConfigureAwait(false);
            }
        }
        var rest = await Task.WhenAll(local).ConfigureAwait(false);
        for (var i = 0; i < drives.Count; i++)
            results[i] ??= rest[i];
        ct.ThrowIfCancellationRequested();
        progress.Report(new TaskStep("Hızlı tarama sonucu okunuyor", 85, null));
        return await Finish(drives, results, progress, ct).ConfigureAwait(false);
    }

    async Task<ScanResult> FastScanDriveAsync(DriveEntry drive, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var response = await SendAsync(new WorkerRequest { Op = Ops.FastScan, Target = drive.Root }, progress, ct).ConfigureAwait(false);
        if (!response.Ok)
            throw new InvalidOperationException(response.Message);
        return await Task.Run(() => ReadTree(response.Payload), ct).ConfigureAwait(false);
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

    public Task<FolderGrowth?> GrowthAsync(ScanResult result, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            return FolderHistory.Compare(FolderHistory.Extract(result), new ScanIndex().Baseline(result.Root.Name, result.FinishedAt));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return null;
        }
    }, ct);

    public Task<IReadOnlyList<Unit>> FindDuplicatesAsync(ScanSnapshot snapshot, IProgress<TaskStep> progress, Action<Unit> found, CancellationToken ct) => Task.Run<IReadOnlyList<Unit>>(() =>
    {
        progress.Report(new TaskStep("Kopya adayları seçiliyor", 0, null));
        var settings = AppSettings.Load();
        var minBytes = settings.DuplicateMinBytes > 0 ? settings.DuplicateMinBytes : DuplicateFinder.DefaultMinBytes;
        var candidates = DuplicateUnits.Candidates(snapshot.Results.Select(r => r.Root), minBytes, snapshot.Units, Protection(null), ct);
        var cache = new HashCache(HashCache.DefaultPath);
        var finder = new DuplicateFinder(cache);
        var units = new List<Unit>();
        try
        {
            finder.Find(candidates, group =>
            {
                var unit = DuplicateUnits.Build(group);
                units.Add(unit);
                found(unit);
            }, new Relay<DuplicateProgress>(p => progress.Report(new TaskStep(p.Step, p.Percent, p.Line))), ct);
        }
        finally
        {
            cache.Save();
        }
        return units;
    }, ct);
    public async Task<ScanSnapshot?> RefreshAsync(ScanSnapshot cached, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var drives = await Task.Run(ListDrives, ct).ConfigureAwait(false);
        if (cached.For(drives[0].Root) is not { Usn: not null, Cancelled: false })
            return null;
        progress.Report(new TaskStep("Değişiklikler okunuyor", -1, "Son taramadan bu yana değişen dosyalar"));
        var tracker = new DriveProgress(Span(progress, 0, 85)!, drives);
        var results = new ScanResult?[drives.Count];
        results[0] = await RefreshOneAsync(cached.For(drives[0].Root)!, progress, ct).ConfigureAwait(false);
        if (results[0] is null)
            return null;
        var rest = await Task.WhenAll(drives.Skip(1).Select((d, i) => RefreshDriveAsync(d, cached.For(d.Root), tracker, i + 1, progress, ct))).ConfigureAwait(false);
        rest.CopyTo(results, 1);
        ct.ThrowIfCancellationRequested();
        return await Finish(drives, results, progress, ct).ConfigureAwait(false);
    }

    async Task<ScanResult?> RefreshDriveAsync(DriveEntry drive, ScanResult? cached, DriveProgress tracker, int index, IProgress<TaskStep> progress, CancellationToken ct)
    {
        if (cached is { Usn: not null, Cancelled: false } && await RefreshOneAsync(cached, progress, ct).ConfigureAwait(false) is { } fresh)
            return fresh;
        return await ScanDriveAsync(drive, new ScanOptions(), tracker.For(index), ct).ConfigureAwait(false);
    }

    async Task<ScanResult?> RefreshOneAsync(ScanResult cached, IProgress<TaskStep> progress, CancellationToken ct)
    {
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
        try
        {
            var update = await Task.Run(() => UsnUpdater.ApplyAsync(copy, ct), ct).ConfigureAwait(false);
            return update.Result;
        }
        catch (UsnJournalResetException)
        {
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return WorkerRunning ? await RefreshInWorkerAsync(copy, progress, ct).ConfigureAwait(false) : null;
        }
    }

    async Task<ScanResult?> RefreshInWorkerAsync(ScanResult copy, IProgress<TaskStep> progress, CancellationToken ct)
    {
        var input = ScanTreeCodec.NewPath(Paths.AppData);
        try
        {
            await Task.Run(() => ScanTreeCodec.Write(copy, input), ct).ConfigureAwait(false);
            var response = await SendAsync(new WorkerRequest { Op = Ops.UsnRefresh, Target = copy.Root.Name, Items = [input] }, Span(progress, 0, 85)!, ct).ConfigureAwait(false);
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
        foreach (var task in WorkerBindings.SystemTasks(Protection(null), System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value))
        {
            if (task.Id == "component-store")
            {
                list.Add(new SystemTaskInfo(task.Id, task.Name, 0, false, "", false, "Ölçümü DISM çalıştırır; arayüz bu görevi başlatmaz"));
                continue;
            }
            try
            {
                var estimate = await Task.Run(() => task.EstimateAsync(ct), ct).ConfigureAwait(false);
                list.Add(new SystemTaskInfo(task.Id, task.Name, estimate.RecoverableBytes, estimate.Recommended, estimate.Detail, estimate.Available, estimate.Note, estimate.Warning, estimate.Silent, estimate.RestoreId, estimate.AltId, estimate.AltLabel, estimate.RestoreLabel));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                list.Add(new SystemTaskInfo(task.Id, task.Name, 0, false, "", true, "Boyut ölçülemedi: " + e.Message, Silent: !task.ExplicitOnly));
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

    public LedgerData AddFreed(long bytes, string? root = null) => _ledger.Add(bytes, DiskCheck.Measure(root ?? ScanRoot));

    public IReadOnlyList<DriveSpace> Drives() => DiskCheck.FixedDrives();

    public bool WeeklyCheck => AppSettings.Load().WeeklyCheck;

    public Task<IntegrationResult> SetWeeklyCheckAsync(bool enabled) => Task.Run(() =>
    {
        try
        {
            (AppSettings.Load() with { WeeklyCheck = enabled }).Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new IntegrationResult(true, false, "Ayar kaydedilemedi: " + e.Message);
        }
        return SystemIntegration.ForCurrentUser().SetWeekly(enabled);
    });

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

    sealed class DriveProgress
    {
        readonly IProgress<TaskStep> _target;
        readonly double[] _weight;
        readonly double[] _percent;

        public DriveProgress(IProgress<TaskStep> target, IReadOnlyList<DriveEntry> drives)
        {
            _target = target;
            var used = drives.Select(d => (double)Math.Max(1, d.UsedBytes)).ToArray();
            var total = used.Sum();
            _weight = [.. used.Select(u => u / total)];
            _percent = new double[drives.Count];
        }

        void Report(int index, string step, double percent, string? line)
        {
            var known = percent is >= 0 and <= 100;
            if (known)
                Volatile.Write(ref _percent[index], percent);
            double sum = 0;
            for (var i = 0; i < _percent.Length; i++)
                sum += _weight[i] * Volatile.Read(ref _percent[i]);
            _target.Report(new TaskStep(step, known ? sum : -1, line));
        }

        public IProgress<ScanProgress> For(int index) => new Relay<ScanProgress>(p => Report(index, p.Step, p.Percent, p.CurrentPath));

        public IProgress<TaskStep> Steps(int index) => new Relay<TaskStep>(p => Report(index, p.Step, p.Percent, p.Line));
    }
}
