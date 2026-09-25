namespace DustyBytes.Clean.Rules;

public interface ICleanupDeleter
{
    bool DeleteFile(string path);
    bool DeleteRegistryValue(string keyPath, string? valueName);
}
