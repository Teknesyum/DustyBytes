using System.Globalization;
using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public sealed record PlanSkip(Unit Unit, string Reason);

public sealed record TargetPlan(long Target, long SafeBytes, IReadOnlyList<Unit> Units, IReadOnlyList<PlanSkip> Skipped, DateTimeOffset Now)
{
    public const string UserDataReason = "Kendi dosyalarınız olabilir";
    public const string UncertainReason = "Emin değiliz";
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public long HeldBytes => Units.Sum(u => Math.Max(0, u.SizeBytes));
    public long TotalBytes => Math.Max(0, SafeBytes) + HeldBytes;
    public long Shortfall => Math.Max(0, Target - TotalBytes);
    public bool IsMet => Shortfall == 0;
    public bool IsEmpty => SafeBytes <= 0 && Units.Count == 0;

    public static string Goal(long bytes) => (bytes / (double)(1L << 30)).ToString("0.#", Tr) + " GB";

    public string HeadText
    {
        get
        {
            var parts = new List<string>();
            if (SafeBytes > 0)
                parts.Add($"güvenli küme {Format.Bytes(SafeBytes)}");
            foreach (var g in Units.GroupBy(u => (Kind: UnitClusters.Group(u.Kind), Band: UnitClusters.Band(u, Now))))
            {
                var bytes = Format.Bytes(g.Sum(u => Math.Max(0, u.SizeBytes)));
                var what = $"{Format.Count(g.Count())} {UnitClusters.Noun(g.Key.Kind)} {bytes}";
                parts.Add(g.Key.Band == AgeBand.Unknown ? $"kullanımı bilinmeyen {what}" : $"{MinMonths(g)} aydır açılmamış {what}");
            }
            return parts.Count == 0
                ? $"{Goal(Target)} için güvenle önerebileceğimiz bir şey yok."
                : $"{Goal(Target)} için: {string.Join(" + ", parts)}";
        }
    }

    int MinMonths(IEnumerable<Unit> units) => Math.Max(3, units.Min(u => u.Usage.LastUsed is { } last ? (int)((Now - last).TotalDays / 30.44) : int.MaxValue));

    public string NowText => HeldBytes > 0
        ? $"Bu planla şimdi {Format.Bytes(Math.Max(0, SafeBytes))} boşalır, {Format.Bytes(HeldBytes)} karantinada {AppSettings.QuarantineDays.Days} gün bekler; hemen gerekiyorsa 'Şimdi yer aç'."
        : $"Bu planla şimdi {Format.Bytes(Math.Max(0, SafeBytes))} boşalır.";

    public string ShortText => IsMet ? ""
        : Skipped.Count > 0
            ? $"Hedefe {Format.Bytes(Shortfall)} eksik kaldı. Aşağıdakiler sorulmadan plana girmez; isterseniz tek tek bakın."
            : $"Hedefe {Format.Bytes(Shortfall)} eksik kaldı; uzun süredir açılmamış başka öğe yok.";
}

public static class TargetPlanner
{
    static readonly AgeBand[] Order = [AgeBand.Over12, AgeBand.Six12, AgeBand.Three6, AgeBand.Unknown];

    public static bool Eligible(Unit unit, DateTimeOffset now) =>
        unit.Removal == RemovalMethod.Quarantine
        && unit.Kind != UnitKind.Duplicate
        && unit.SizeBytes > 0
        && UnitClusters.Band(unit, now) != AgeBand.Recent;

    public static TargetPlan Build(long target, long safeBytes, IEnumerable<Unit> units, DateTimeOffset now, IReadOnlySet<string>? skip = null)
    {
        var pool = units.Where(u => Eligible(u, now) && (skip is null || !skip.Contains(u.Id))).ToList();
        var skipped = pool.Where(u => u.ContainsUserData || UnitClusters.Uncertain(u))
            .OrderByDescending(u => u.SizeBytes)
            .Select(u => new PlanSkip(u, UnitClusters.Uncertain(u) ? TargetPlan.UncertainReason : TargetPlan.UserDataReason))
            .ToList();
        var candidates = pool.Where(u => !u.ContainsUserData && !UnitClusters.Uncertain(u)).ToList();
        var chosen = new List<Unit>();
        var remaining = target - Math.Max(0, safeBytes);
        foreach (var band in Order)
        {
            if (remaining <= 0)
                break;
            var items = candidates.Where(u => UnitClusters.Band(u, now) == band).OrderByDescending(u => u.SizeBytes).ToList();
            while (remaining > 0 && items.Count > 0)
            {
                var cover = items.LastOrDefault(u => u.SizeBytes >= remaining);
                var pick = cover ?? items[0];
                chosen.Add(pick);
                items.Remove(pick);
                remaining -= pick.SizeBytes;
            }
        }
        return new TargetPlan(target, Math.Max(0, safeBytes), chosen, skipped, now);
    }
}
