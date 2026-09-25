using DustyBytes.Clean.Rules;

namespace DustyBytes.Rules.Tests;

public sealed class FakeDeleter : ICleanupDeleter
{
    public List<string> Deleted { get; } = [];
    public List<(string Key, string? Value)> DeletedRegistry { get; } = [];

    public bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        Deleted.Add(path);
        return true;
    }

    public bool DeleteRegistryValue(string keyPath, string? valueName)
    {
        DeletedRegistry.Add((keyPath, valueName));
        return true;
    }
}
