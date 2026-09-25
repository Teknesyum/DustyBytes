namespace DustyBytes.Core.Model;

public sealed record UsageSignal(DateTimeOffset? LastUsed, string Source, double Reliability)
{
    public static readonly UsageSignal Unknown = new(null, "bilinmiyor", 0);

    public bool IsKnown => LastUsed is not null;
}
