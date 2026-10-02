using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.SpaceSaver;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.App.Services;

public sealed record TaskStep(string Step, double Percent, string? Line);

public sealed record ScanSnapshot(ScanResult Result, IReadOnlyList<Unit> Units, DateTimeOffset FinishedAt, string Method)
{
    public IReadOnlyList<ScanResult> Drives { get; init; } = [];
    public IReadOnlyList<DriveEntry> Volumes { get; init; } = [];

    public IReadOnlyList<ScanResult> Results => Drives.Count > 0 ? Drives : [Result];

    public long Files => Results.Sum(r => r.Files);

    public ScanResult? For(string root) =>
        Results.FirstOrDefault(r => r.Root.Name.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
}

public sealed record ProgramInfo(InstalledProgram Program, UsageSignal Usage);

public sealed record CleanRuleInfo(CleanerRule Rule, bool Running, string? RunningReason);

public sealed record SystemTaskInfo(string Id, string Name, long Bytes, bool Recommended, string Detail, bool Available, string? Note, string? Warning = null, bool Silent = true, string? RestoreId = null, string? AltId = null, string? AltLabel = null, string? RestoreLabel = null);

public sealed record QuarantineSnapshot(IReadOnlyList<QuarantineEntry> Entries, IReadOnlyList<VolumeUsage> Usage, bool Complete, string? Note);

public sealed record Availability(bool Enabled, string Reason);

public interface IAppBackend
{
    string ScanRoot { get; }
    bool DryRun { get; }
    bool WorkerRunning { get; }
    bool Winapp2Present { get; }
    bool ScanRemovable { get; set; }

    Task<ScanSnapshot?> LoadCachedAsync(IProgress<TaskStep>? progress, CancellationToken ct);
    Task<ScanSnapshot> ScanAsync(IProgress<TaskStep> progress, CancellationToken ct, IProgress<ScanDraft>? drafts = null);
    Task<ScanSnapshot?> RefreshAsync(ScanSnapshot cached, IProgress<TaskStep> progress, CancellationToken ct);
    Availability FastScanAvailability();
    Task<ScanSnapshot> FastScanAsync(IProgress<TaskStep> progress, CancellationToken ct);
    Task<FolderGrowth?> GrowthAsync(ScanResult result, CancellationToken ct);
    Task<IReadOnlyList<Unit>> FindDuplicatesAsync(ScanSnapshot snapshot, IProgress<TaskStep> progress, Action<Unit> found, CancellationToken ct);

    Task<IReadOnlyList<ProgramInfo>> ListProgramsAsync(IProgress<TaskStep> progress, CancellationToken ct);
    Task<LeftoverSnapshot> PreviewLeftoversAsync(InstalledProgram program, IProgress<TaskStep> progress, CancellationToken ct);

    Task<IReadOnlyList<CleanRuleInfo>> CleanRulesAsync(CancellationToken ct);
    Task<IReadOnlyList<OptionPreview>> PreviewCleanAsync(IReadOnlyList<RuleSelection> selection, CancellationToken ct);
    Task<CleanPreview> PreviewCleanFilesAsync(IReadOnlyList<string> optionKeys, CancellationToken ct);
    Task<CleanPreview> PreviewCleanInWorkerAsync(IReadOnlyList<string> optionKeys, IProgress<TaskStep> progress, CancellationToken ct);
    Task<IReadOnlyList<SystemTaskInfo>> SystemTasksAsync(CancellationToken ct);

    Task<QuarantineSnapshot> ReadQuarantineAsync(CancellationToken ct);
    Task<CompressionEstimate> EstimateCompressionAsync(Unit unit, CancellationToken ct);
    Task<WorkerResponse> SendAsync(WorkerRequest request, IProgress<TaskStep> progress, CancellationToken ct);

    LedgerData ReadLedger();
    LedgerData AddFreed(long bytes, string? root = null);

    IReadOnlyList<DriveSpace> Drives();
    bool WeeklyCheck { get; }
    Task<IntegrationResult> SetWeeklyCheckAsync(bool enabled);
}
