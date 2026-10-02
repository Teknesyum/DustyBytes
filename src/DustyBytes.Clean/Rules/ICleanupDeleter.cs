namespace DustyBytes.Clean.Rules;

public interface ICleanupDeleter
{
    bool DeleteFile(string path);
    bool DeleteRegistryValue(string keyPath, string? valueName);

    DeleteOutcome TryDeleteFile(string path) => DeleteFile(path) ? DeleteOutcome.Deleted : DeleteOutcome.Failed;
}

public sealed class ShownOnlyDeleter(ICleanupDeleter inner, IReadOnlySet<string> shown) : ICleanupDeleter
{
    public List<string> Attempted { get; } = [];

    void Guard(string path)
    {
        if (!shown.Contains(path))
            throw new ShownListViolationException(path);
        Attempted.Add(path);
    }

    public bool DeleteFile(string path)
    {
        Guard(path);
        return inner.DeleteFile(path);
    }

    public DeleteOutcome TryDeleteFile(string path)
    {
        Guard(path);
        return inner.TryDeleteFile(path);
    }

    public bool DeleteRegistryValue(string keyPath, string? valueName)
    {
        Guard(CleanerCatalog.RegistryEntry(keyPath, valueName));
        return inner.DeleteRegistryValue(keyPath, valueName);
    }
}
