using DustyBytes.Core.Ipc;

namespace DustyBytes.Clean.Rules;

public sealed record OptionPreview(string RuleId, string OptionId, long FileCount, long Bytes);

public sealed record SkippedPath(string Path, string Reason);

public sealed record OptionExecutionResult(
    string RuleId,
    string OptionId,
    bool Ran,
    string? SkipReason,
    long DeletedFiles,
    long DeletedBytes,
    IReadOnlyList<SkippedPath> Skipped,
    bool DryRun);

public sealed record RuleSelection(string RuleId, IReadOnlyList<string> OptionIds);

public sealed record PlannedFile(string RuleId, string OptionId, string Path, long Bytes, DateTime LastWriteUtc, CleanActionType Type = CleanActionType.Delete, string? Key = null, string? Value = null)
{
    public string Option => $"{RuleId}/{OptionId}";
    public bool IsRegistry => Type == CleanActionType.RegistryDelete;
}

public sealed record CleanPlan(IReadOnlyList<PlannedFile> Files, IReadOnlyList<SkippedPath> Excluded)
{
    public long Bytes => Files.Sum(f => f.Bytes);

    public CleanPreview ToPreview() =>
        CleanPreview.Of(Files.Select(f => new PreviewFile(f.Path, f.Bytes, f.LastWriteUtc, f.Option)), Excluded.Count);
}

public sealed record CleanRun(IReadOnlyList<OptionExecutionResult> Options, PathTally Tally, string? Error);

public enum DeleteOutcome
{
    Deleted,
    Vanished,
    Locked,
    Failed,
}

public sealed class ShownListViolationException(string path)
    : InvalidOperationException($"Önizlemede gösterilmeyen yol silinmek istendi; işlem durduruldu: {path}")
{
    public string Path { get; } = path;
}
