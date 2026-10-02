using System.Globalization;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public enum AgeBand
{
    Over12,
    Six12,
    Three6,
    Unknown,
    Recent,
}

public sealed record UnitGroup(UnitKind Kind, AgeBand Band, IReadOnlyList<Unit> Units)
{
    public long Bytes => Units.Sum(u => Math.Max(0, u.SizeBytes));
    public string Title => UnitClusters.Title(this);
}

public static class UnitClusters
{
    public const double SureConfidence = 0.6;
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static bool Uncertain(Unit unit) => unit.Confidence < SureConfidence;

    public static AgeBand Band(Unit unit, DateTimeOffset now)
    {
        if (unit.Usage.LastUsed is not { } last)
            return AgeBand.Unknown;
        var days = (now - last).TotalDays;
        return days >= 365 ? AgeBand.Over12
            : days >= 182 ? AgeBand.Six12
            : days >= TourViewModel.LongUnused.TotalDays ? AgeBand.Three6
            : AgeBand.Recent;
    }

    public static int Months(AgeBand band) => band switch
    {
        AgeBand.Over12 => 12,
        AgeBand.Six12 => 6,
        AgeBand.Three6 => 3,
        _ => 0,
    };

    public static UnitKind Group(UnitKind kind) => kind switch
    {
        UnitKind.Series => UnitKind.Film,
        UnitKind.BrowserCache => UnitKind.Cache,
        _ => kind,
    };

    public static string Noun(UnitKind kind) => KindText.Label(Group(kind)).ToLower(Tr);

    public static string BandText(AgeBand band) => band switch
    {
        AgeBand.Over12 => "12+ aydır açılmamış",
        AgeBand.Six12 => "6–12 aydır açılmamış",
        AgeBand.Three6 => "3–6 aydır açılmamış",
        AgeBand.Unknown => "ne zaman açıldığı bilinmiyor",
        _ => "yakında açılmış",
    };

    public static string Title(UnitGroup group) => $"{Format.Count(group.Units.Count)} {Noun(group.Kind)}, {BandText(group.Band)}";

    public static (IReadOnlyList<UnitGroup> Groups, IReadOnlyList<Unit> Uncertain) Build(IEnumerable<Unit> units, DateTimeOffset now)
    {
        var list = units.ToList();
        var groups = list.Where(u => !Uncertain(u))
            .GroupBy(u => (Kind: Group(u.Kind), Band: Band(u, now)))
            .Select(g => new UnitGroup(g.Key.Kind, g.Key.Band, [.. g.OrderByDescending(u => u.SizeBytes)]))
            .OrderByDescending(g => g.Bytes)
            .ToList();
        var unsure = list.Where(Uncertain).OrderByDescending(u => u.SizeBytes).ToList();
        return (groups, unsure);
    }

    public static int Decisions(IEnumerable<Unit> units, DateTimeOffset now)
    {
        var (groups, unsure) = Build(units, now);
        return groups.Count + unsure.Count;
    }
}
