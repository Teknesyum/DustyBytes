using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public sealed class BrowserCacheExtractor : IUnitExtractor
{
    sealed record BrowserSpec(string Name, string ProfilesRootPattern, string[] CacheSubdirs);

    static readonly BrowserSpec[] Browsers =
    [
        new("Chrome", @"Users\*\AppData\Local\Google\Chrome\User Data",
            ["Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage"]),
        new("Edge", @"Users\*\AppData\Local\Microsoft\Edge\User Data",
            ["Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage"]),
        new("Brave", @"Users\*\AppData\Local\BraveSoftware\Brave-Browser\User Data",
            ["Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage"]),
        new("Firefox", @"Users\*\AppData\Local\Mozilla\Firefox\Profiles",
            ["cache2", "startupCache"]),
    ];

    internal static IEnumerable<(string Pattern, string Group)> Roots => Browsers.Select(b => (b.ProfilesRootPattern, "tarayıcı:" + b.Name));

    public IEnumerable<Unit> Extract(UnitContext ctx)
    {
        var units = new List<Unit>();

        foreach (var spec in Browsers)
        {
            var paths = new List<string>();
            long size = 0;
            DateTimeOffset? newest = null;

            foreach (var profilesRoot in PathPattern.Match(ctx.Root, spec.ProfilesRootPattern))
            {
                var profiles = profilesRoot.Children?.Where(c => c.IsDirectory) ?? [];
                foreach (var profile in profiles)
                {
                    foreach (var subdir in spec.CacheSubdirs)
                    {
                        var node = profile.ResolveRelative(subdir);
                        if (node is null || node.Size <= 0)
                            continue;

                        paths.Add(node.FullPath);
                        size += node.Size;
                        if (node.NewestWriteTicks > 0 && (newest is null || node.NewestWrite > newest))
                            newest = node.NewestWrite;
                    }
                }
            }

            if (paths.Count == 0 || size <= 0)
                continue;

            var usage = newest is { } n
                ? new UsageSignal(n, "tarayıcı önbelleği son yazma", 0.4)
                : UsageSignal.Unknown;

            var unit = new Unit
            {
                Id = UnitIdentity.Compute(UnitKind.BrowserCache, paths[0]),
                Kind = UnitKind.BrowserCache,
                Name = $"{spec.Name} önbelleği",
                Paths = paths,
                SizeBytes = size,
                Usage = usage,
                Confidence = 1.0,
                Removal = RemovalMethod.DirectDelete,
            };
            units.Add(unit with { Reason = Scoring.Reason(unit, ctx.Now) });
        }

        return units;
    }
}
