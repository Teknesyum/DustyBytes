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
