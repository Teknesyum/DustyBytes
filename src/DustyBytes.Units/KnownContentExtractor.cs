using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class KnownContentExtractor(KnownContentRules? rules = null) : IUnitExtractor
{
    readonly Lazy<KnownContentRules> _rules = new(() => rules ?? KnownContentRules.LoadDefault());

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        foreach (var item in _rules.Value.Items)
        {
            var nodes = item.Patterns
                .SelectMany(p => PathPattern.Match(ctx.Root, p))
                .DistinctBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (item.Each)
            {
                foreach (var node in nodes.Where(n => n.Size >= item.MinBytes))
                    units.Add(Make(ctx, item, Pretty(node), [node]));
            }
            else
            {
                foreach (var group in nodes.GroupBy(UserOf, StringComparer.OrdinalIgnoreCase))
                {
                    var list = group.ToList();
                    if (list.Sum(n => n.Size) >= item.MinBytes)
                        units.Add(Make(ctx, item, Title(item), list));
                }
            }
        }
        return units;
    }

    static Unit Make(UnitContext ctx, KnownContent item, string name, IReadOnlyList<ScanNode> nodes)
    {
        var newest = nodes.Max(n => n.NewestWriteTicks);
        var usage = ctx.UsageIndex.ForFolder(nodes[0].FullPath);
        if (usage.LastUsed is null && newest > 0)
            usage = new UsageSignal(new DateTimeOffset(newest, TimeSpan.Zero), "write", 0.3);
        var unit = new Unit
        {
            Id = UnitIdentity.Compute(UnitKind.AppContent, nodes[0].FullPath),
            Kind = UnitKind.AppContent,
            Name = name,
            Label = item.Owner + " · " + item.What,
            Paths = nodes.Select(n => n.FullPath).ToList(),
            SizeBytes = nodes.Sum(n => n.Size),
            Usage = usage,
            Confidence = 1.0,
            Removal = RemovalMethod.Quarantine,
            ContainsUserData = item.UserData,
            Effect = item.Effect,
        };
        return unit with { Reason = Scoring.Reason(unit, ctx.Now) };
    }

    static string Title(KnownContent item) =>
        item.Owner + " " + item.What.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    static string Pretty(ScanNode node)
    {
        var name = node.Name;
        if (name.Equals("LocalState", StringComparison.OrdinalIgnoreCase) && node.Parent is { } package)
        {
            name = package.Name;
            var cut = name.IndexOf('_');
            if (cut > 0)
                name = name[..cut];
            var dot = name.IndexOf('.');
            if (dot >= 0 && dot < name.Length - 1)
                name = name[(dot + 1)..];
        }
        if (name.StartsWith("models--", StringComparison.OrdinalIgnoreCase))
            name = name["models--".Length..].Replace("--", "/");
        if (name.StartsWith("datasets--", StringComparison.OrdinalIgnoreCase))
            name = name["datasets--".Length..].Replace("--", "/");
        if (name.EndsWith(".avd", StringComparison.OrdinalIgnoreCase))
            name = name[..^4].Replace('_', ' ');
        return name;
    }

    static string UserOf(ScanNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (n.Parent is { Name: "Users" })
                return n.Name;
        return "";
    }
}
