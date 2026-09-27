using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.Units;

public sealed class DevArtifactExtractor : IUnitExtractor
{
    readonly DevArtifactRules _rules;
    readonly HashSet<string> _exact;
    readonly HashSet<string> _extensions;
    readonly bool _complex;

    public DevArtifactExtractor(DevArtifactRules? rules = null)
    {
        _rules = rules ?? DevArtifactRules.LoadDefault();
        var markers = _rules.Ecosystems.SelectMany(e => e.Markers).ToList();
        _exact = new(markers.Where(m => !m.StartsWith('*')), StringComparer.OrdinalIgnoreCase);
        _extensions = new(markers.Where(m => m.StartsWith('*') && Simple(m)).Select(m => m[1..]), StringComparer.OrdinalIgnoreCase);
        _complex = markers.Any(m => !Simple(m));
    }

    public static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(1);

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();
        var skip = new HashSet<ScanNode>();

        var stack = new Stack<ScanNode>();
        stack.Push(ctx.Root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (skip.Contains(dir))
                continue;

            var files = FileNames(dir);
            foreach (var eco in files is null ? [] : _rules.Ecosystems)
            {
                if (!eco.Markers.Any(m => Has(files!, dir, m)))
                    continue;

                var artifactNodes = new List<ScanNode>();
                foreach (var rel in eco.ArtifactDirs)
                {
                    var node = dir.ResolveRelative(rel);
                    if (node is not null)
                        artifactNodes.Add(node);
                }

                if (artifactNodes.Count == 0)
                    continue;

                var size = artifactNodes.Sum(n => n.Size);
                if (size <= 0)
                    continue;

                foreach (var n in artifactNodes)
                    skip.Add(n);

                var lastUsed = dir.NewestWriteTicks > 0 ? dir.NewestWrite : (DateTimeOffset?)null;
                if (lastUsed is { } recent && ctx.Now - recent < ActiveWindow)
                    continue;
                var usage = new UsageSignal(lastUsed, "proje kaynakları son yazma", 0.6);
                var reason = lastUsed is { } used
                    ? $"Proje {dir.Name}, son değişiklik {Format.Ago(used, ctx.Now)}"
                    : $"Proje {dir.Name}, son değişiklik bilinmiyor";

                var unit = new Unit
                {
                    Id = UnitIdentity.Compute(UnitKind.DevArtifact, dir.FullPath),
                    Kind = UnitKind.DevArtifact,
                    Name = $"{dir.Name} ({eco.Name})",
                    Paths = artifactNodes.Select(n => n.FullPath).ToList(),
                    SizeBytes = size,
                    Usage = usage,
                    Confidence = 0.9,
                    Removal = RemovalMethod.DirectDelete,
                    Reason = reason,
                };
                units.Add(unit);
            }

            if (dir.Children is null)
                continue;
            foreach (var child in dir.Children)
                if (child.IsDirectory && !skip.Contains(child))
                    stack.Push(child);
        }

        return units;
    }

    HashSet<string>? FileNames(ScanNode dir)
    {
        HashSet<string>? found = null;
        if (dir.Children is not { } kids)
            return null;
        var exact = _exact.GetAlternateLookup<ReadOnlySpan<char>>();
        var ext = _extensions.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var c in kids)
        {
            if (c.IsDirectory)
                continue;
            var name = c.Name.AsSpan();
            var dot = name.LastIndexOf('.');
            if (exact.Contains(name))
                (found ??= new(StringComparer.OrdinalIgnoreCase)).Add(c.Name);
            if (dot >= 0 && ext.Contains(name[dot..]))
                (found ??= new(StringComparer.OrdinalIgnoreCase)).Add("*" + c.Name[dot..]);
            if (_complex && found is null)
                found = new(StringComparer.OrdinalIgnoreCase);
        }
        return found;
    }

    static bool Simple(string marker) =>
        !marker.StartsWith('*') || marker.StartsWith("*.", StringComparison.Ordinal) && marker.IndexOf('.', 2) < 0;

    static bool Has(HashSet<string> files, ScanNode dir, string marker) =>
        Simple(marker) ? files.Contains(marker) : dir.MatchesMarker(marker);
}
