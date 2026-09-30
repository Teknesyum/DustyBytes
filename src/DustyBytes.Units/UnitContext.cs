using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;

namespace DustyBytes.Units;

public sealed record UnitContext
{
    public required ScanResult ScanResult { get; init; }
    public required IUsageIndex UsageIndex { get; init; }
    public required ProtectedList Protected { get; init; }
    public required DateTimeOffset Now { get; init; }
    public IReadOnlyList<ProgramInstall> Programs { get; init; } = [];
    public Func<string, bool>? CloudEligible { get; init; }

    public ScanNode Root => ScanResult.Root;
}
