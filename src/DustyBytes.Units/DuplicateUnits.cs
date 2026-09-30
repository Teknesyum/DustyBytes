using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan.Duplicates;
using CorePaths = DustyBytes.Core.Paths;

namespace DustyBytes.Units;

public static class DuplicateUnits
{
    public const string Title = "Kopyalar";
    public const string Label = "Kopya";

    public const string Rule =
        "İndirilenler ve geçici klasörler dışındaki en eski kopya kalır, diğerleri önerilir. İstersen kalacak kopyayı değiştirirsin.";

    public const string Effect =
        "Fazla kopyalar karantinaya taşınır, kalıcı silinmez. Taşımadan hemen önce her kopya kalacak dosyayla bayt bayt yeniden karşılaştırılır; fark varsa dokunulmaz.";

    static readonly string[] TransientParts =
    [
        @"\Downloads\",
        @"\Temp\",
        @"\Tmp\",
        @"\AppData\Local\Temp\",
    ];

    static readonly string[] SkippedFolderNames =
    [
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "AppData",
        "$Recycle.Bin",
        "System Volume Information",
        "Recovery",
        "$WinREAgent",
        "WindowsApps",
        "steamapps",
        "Steam",
        "SteamLibrary",
        "Epic Games",
        "XboxGames",
        "GOG Games",
        "node_modules",
        ".git",
        CorePaths.QuarantineDir,
    ];

    static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".sys", ".drv", ".ocx", ".cat", ".mui", ".vhd", ".vhdx", ".pst", ".ost",
    };

    static readonly HashSet<UnitKind> OwnedKinds = [UnitKind.Game, UnitKind.Program, UnitKind.AppContent, UnitKind.DevArtifact];

    public static bool IsTransient(string path)
    {
        var p = CorePaths.Normalize(path) + @"\";
        return TransientParts.Any(part => p.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    public static string PickKeep(IReadOnlyList<DuplicateCopy> copies) =>
        copies
            .OrderBy(c => IsTransient(c.Path) ? 1 : 0)
            .ThenBy(c => c.Created)
            .ThenBy(c => c.Path.Length)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .First().Path;

    public static string IdFor(DuplicateGroup group) => $"{UnitKind.Duplicate}-{group.Hash[..16]}";

    public static Unit Build(DuplicateGroup group)
    {
        var keep = PickKeep(group.Copies);
        var others = group.Copies.Where(c => !c.Path.Equals(keep, StringComparison.OrdinalIgnoreCase)).ToList();
        return new Unit
        {
            Id = IdFor(group),
            Kind = UnitKind.Duplicate,
            Name = Path.GetFileName(keep),
            Label = Label,
            Keep = keep,
            Paths = [.. others.Select(c => c.Path)],
            SizeBytes = others.Sum(c => c.Allocated),
            Confidence = 0.9,
            Removal = RemovalMethod.Quarantine,
            ContainsUserData = true,
            Reason = ReasonFor(group.Copies.Count),
            Effect = Effect,
        };
    }

    public static string ReasonFor(int copies) => $"Aynı içerikte {copies} dosya; {copies - 1} tanesi fazla";

    public static IReadOnlyList<string> Copies(Unit unit) =>
        unit.Keep is { } keep ? [keep, .. unit.Paths] : unit.Paths;

    public static Unit WithKeep(Unit unit, string keep)
    {
        if (unit.Kind != UnitKind.Duplicate || unit.Keep is null)
            return unit;
        var all = Copies(unit);
        var chosen = all.FirstOrDefault(p => p.Equals(keep, StringComparison.OrdinalIgnoreCase));
        if (chosen is null || chosen.Equals(unit.Keep, StringComparison.OrdinalIgnoreCase))
            return unit;
        return unit with
        {
            Keep = chosen,
            Name = Path.GetFileName(chosen),
            Paths = [.. all.Where(p => !p.Equals(chosen, StringComparison.OrdinalIgnoreCase))],
        };
    }

    public static bool SkipFile(string path) => SkippedExtensions.Contains(Path.GetExtension(path));

    public static Func<string, bool> SkipFolder(IEnumerable<Unit> owners)
    {
        var owned = owners
            .Where(u => OwnedKinds.Contains(u.Kind))
            .SelectMany(u => u.Paths)
            .Select(CorePaths.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = SkippedFolderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return folder =>
        {
            if (names.Contains(Path.GetFileName(folder.TrimEnd('\\'))))
                return true;
            return owned.Contains(CorePaths.Normalize(folder));
        };
    }

    public static List<DuplicateCandidate> Candidates(IEnumerable<Scan.ScanNode> roots, long minBytes, IEnumerable<Unit> owners, ProtectedList? protectedList, CancellationToken ct) =>
        [.. DuplicateFinder.Candidates(roots, minBytes, SkipFolder(owners), ct)
            .Where(c => !SkipFile(c.Path) && (protectedList is null || protectedList.CheckPath(c.Path).Allowed))];
}
