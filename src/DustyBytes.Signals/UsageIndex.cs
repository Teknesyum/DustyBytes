using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.Signals;

public sealed record SignalReport(string Source, int Count, string? Error);

public sealed class UsageIndex : IUsageIndex
{
    private static readonly string[] NonUseMarkers =
        ["unins", "setup", "redist", "vcredist", "dxsetup", "crashhandler", "crashreport", "crashpad", "installer", "updater"];

    private readonly Dictionary<string, List<(ExecutableRun Run, double Reliability)>> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaPlay> _media = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameInstall> _games = [];
    private readonly List<string> _roots = [];
    private readonly List<SignalReport> _reports = [];

    private UsageIndex()
    {
    }

    public IReadOnlyList<GameInstall> Games => _games;
    public IReadOnlyList<string> LibraryRoots => _roots;
    public IReadOnlyList<SignalReport> Reports => _reports;
    public int ExecutableCount => _runs.Count;
    public int MediaCount => _media.Count;

    public static IReadOnlyList<IGameLibrary> DefaultLibraries() =>
        [new SteamLibrary(), new EpicLibrary(), new GogLibrary(), PresenceLibrary.BattleNet(), PresenceLibrary.Ea(), new UbisoftLibrary()];

    public static IReadOnlyList<IExecutableSignal> DefaultExecutableSignals() =>
        [new PrefetchSignal(), new UserAssistSignal()];

    public static IReadOnlyList<IMediaSignal> DefaultMediaSignals() =>
        [new VlcSignal(), new MpcHcSignal()];

    public static UsageIndex Collect(
        IEnumerable<IGameLibrary>? libraries = null,
        IEnumerable<IExecutableSignal>? executableSignals = null,
        IEnumerable<IMediaSignal>? mediaSignals = null)
    {
        var index = new UsageIndex();

        foreach (var signal in executableSignals ?? DefaultExecutableSignals())
        {
            var name = SafeName(() => signal.Source, "exe");
            try
            {
                var runs = signal.Read();
                foreach (var run in runs)
                    index.AddRun(run, signal.Reliability);
                index._reports.Add(new SignalReport(name, runs.Count, signal is PrefetchSignal { Failed: > 0 } p ? $"{p.Failed} dosya okunamadı" : null));
            }
            catch (Exception ex)
            {
                index._reports.Add(new SignalReport(name, 0, ex.Message));
            }
        }

        foreach (var signal in mediaSignals ?? DefaultMediaSignals())
        {
            var name = SafeName(() => signal.Source, "medya");
            try
            {
                var plays = signal.Read();
                foreach (var play in plays)
                    index.AddMedia(play);
                index._reports.Add(new SignalReport(name, plays.Count, null));
            }
            catch (Exception ex)
            {
                index._reports.Add(new SignalReport(name, 0, ex.Message));
            }
        }

        foreach (var library in libraries ?? DefaultLibraries())
        {
            var name = SafeName(() => library.Launcher, "launcher");
            try
            {
                foreach (var root in library.LibraryRoots())
                    if (!index._roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                        index._roots.Add(root);
                var games = library.ReadInstalls();
                foreach (var game in games)
                    index._games.Add(index.Enrich(game));
                index._reports.Add(new SignalReport(name, games.Count, null));
            }
            catch (Exception ex)
            {
                index._reports.Add(new SignalReport(name, 0, ex.Message));
            }
        }

        return index;
    }

    public UsageSignal ForExecutable(string exePath)
    {
        var key = Key(exePath);
        return key is not null && _runs.TryGetValue(key, out var list) ? Best(list) : UsageSignal.Unknown;
    }

    public UsageSignal ForFolder(string folder)
    {
        var candidates = RunsUnder(folder).ToList();
        var best = candidates.Count > 0 ? Best(candidates) : UsageSignal.Unknown;
        foreach (var game in _games)
        {
            if (game.LastPlayed is not { } played || game.Launcher != "Steam" || !(SafeIsUnder(folder, game.InstallDir) || SafeIsUnder(game.InstallDir, folder)))
                continue;
            if (best.LastUsed is null || played > best.LastUsed)
                best = new UsageSignal(played, SourceNames.SteamLastPlayed, Reliability.LauncherLastPlayed);
        }
        return best;
    }

    public UsageSignal ForMedia(string filePath)
    {
        var key = Key(filePath);
        if (key is null || !_media.TryGetValue(key, out var play))
            return UsageSignal.Unknown;
        return new UsageSignal(play.LastPlayed, play.Source, Reliability.MediaPlayer);
    }

    public UsageSignal ForGame(GameInstall game)
    {
        if (game.Launcher == "Steam" && game.LastPlayed is { } played)
        {
            var folder = ForFolder(game.InstallDir);
            return folder.LastUsed > played ? folder : new UsageSignal(played, SourceNames.SteamLastPlayed, Reliability.LauncherLastPlayed);
        }
        return ForFolder(game.InstallDir);
    }

    private GameInstall Enrich(GameInstall game)
    {
        var runs = RunsUnder(game.InstallDir).ToList();
        var exes = game.Executables
            .Concat(runs.Select(r => r.Run.ExePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = game with { Executables = exes };
        if (game.LastPlayed is null && runs.Count > 0)
            result = result with { LastPlayed = Best(runs).LastUsed };
        return result;
    }

    private IEnumerable<(ExecutableRun Run, double Reliability)> RunsUnder(string folder)
    {
        var root = Key(folder);
        if (root is null)
            yield break;
        var prefix = root.EndsWith('\\') ? root : root + '\\';
        foreach (var (path, list) in _runs)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            if (NonUseMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (var item in list)
                yield return item;
        }
    }

    private static UsageSignal Best(IEnumerable<(ExecutableRun Run, double Reliability)> list)
    {
        var best = list
            .OrderByDescending(x => x.Run.LastRun)
            .ThenByDescending(x => x.Reliability)
            .First();
        return new UsageSignal(best.Run.LastRun, best.Run.Source, best.Reliability);
    }

    private void AddRun(ExecutableRun run, double reliability)
    {
        var key = Key(run.ExePath);
        if (key is null)
            return;
        if (!_runs.TryGetValue(key, out var list))
            _runs[key] = list = [];
        list.Add((run with { ExePath = key }, reliability));
    }

    private void AddMedia(MediaPlay play)
    {
        var key = Key(play.FilePath);
        if (key is null)
            return;
        if (!_media.TryGetValue(key, out var existing) || (play.LastPlayed is not null && (existing.LastPlayed is null || play.LastPlayed > existing.LastPlayed)))
            _media[key] = play with { FilePath = key };
    }

    private static string? Key(string path)
    {
        try
        {
            return Paths.Normalize(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeIsUnder(string path, string root)
    {
        try
        {
            return Paths.IsUnder(path, root);
        }
        catch
        {
            return false;
        }
    }

    private static string SafeName(Func<string> get, string fallback)
    {
        try
        {
            return get();
        }
        catch
        {
            return fallback;
        }
    }
}
