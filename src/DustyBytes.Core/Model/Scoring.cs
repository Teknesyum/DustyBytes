namespace DustyBytes.Core.Model;

public static class Scoring
{
    public const double UnknownIdleWeight = 0.5;

    public static double IdleWeight(UsageSignal usage, DateTimeOffset now)
    {
        if (usage.LastUsed is not { } last)
            return UnknownIdleWeight;
        var days = Math.Max(0, (now - last).TotalDays);
        return Math.Clamp(Math.Log10(1 + days) / Math.Log10(1 + 730), 0.02, 1.0);
    }

    public static double Score(long sizeBytes, UsageSignal usage, double confidence, DateTimeOffset now)
    {
        if (sizeBytes <= 0)
            return 0;
        var sizeTerm = Math.Log2(1 + sizeBytes / 1_048_576.0);
        return sizeTerm * IdleWeight(usage, now) * Math.Clamp(confidence, 0, 1);
    }

    public static Unit WithScore(Unit unit, DateTimeOffset now) =>
        unit with { Score = Score(unit.SizeBytes, unit.Usage, unit.Confidence, now) };

    public static string Reason(Unit unit, DateTimeOffset now)
    {
        var size = Format.Bytes(unit.SizeBytes);
        if (unit.Usage.LastUsed is not { } last)
            return $"Son kullanım bilinmiyor, {size}";
        var verb = unit.Kind switch
        {
            UnitKind.Game => "Son oynanma",
            UnitKind.Film or UnitKind.Series => "Son izlenme",
            UnitKind.Program => "Son çalışma",
            _ => "Son değişiklik",
        };
        return $"{verb} {Format.Ago(last, now)}, {size}";
    }
}
