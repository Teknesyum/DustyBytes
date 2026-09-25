using DustyBytes.Core.Model;

namespace DustyBytes.Signals;

public sealed record GameInstall
{
    public required string Launcher { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstallDir { get; init; }
    public long? SizeOnDisk { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }
    public string? UninstallUri { get; init; }
    public IReadOnlyList<string> Executables { get; init; } = [];
    public IReadOnlyList<string> SaveDirs { get; init; } = [];
}

public sealed record ExecutableRun(string ExePath, DateTimeOffset LastRun, int RunCount, string Source);

public sealed record MediaPlay(string FilePath, DateTimeOffset? LastPlayed, string Source);

public interface IGameLibrary
{
    string Launcher { get; }
    IReadOnlyList<string> LibraryRoots();
    IReadOnlyList<GameInstall> ReadInstalls();
}

public interface IExecutableSignal
{
    string Source { get; }
    double Reliability { get; }
    IReadOnlyList<ExecutableRun> Read();
}

public interface IMediaSignal
{
    string Source { get; }
    IReadOnlyList<MediaPlay> Read();
}

public interface IUsageIndex
{
    UsageSignal ForExecutable(string exePath);
    UsageSignal ForFolder(string folder);
    UsageSignal ForMedia(string filePath);
    IReadOnlyList<GameInstall> Games { get; }
}
