using System.Diagnostics;

namespace DustyBytes.Clean.Uninstall;

public sealed record FileIdentity(string? CompanyName, string? ProductName, string? Signer, bool SignatureTrusted);

public interface IFileProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileIdentity? Identity(string file);
    byte[]? ReadHead(string file, int max);
    string? VersionText(string file) => null;
}

public sealed class FileProbe : IFileProbe
{
    public static readonly FileProbe Instance = new();

    readonly Dictionary<string, FileIdentity?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly Lock _gate = new();

    public bool CheckSignatures { get; init; } = true;

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileIdentity? Identity(string file)
    {
        lock (_gate)
            if (_cache.TryGetValue(file, out var hit))
                return hit;

        FileIdentity? result = null;
        try
        {
            if (File.Exists(file))
            {
                var info = FileVersionInfo.GetVersionInfo(file);
                var signer = CheckSignatures ? Authenticode.GetSigner(file) : null;
                result = new FileIdentity(
                    string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim(),
                    string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName.Trim(),
                    signer?.Name,
                    signer?.Trusted ?? false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            result = null;
        }

        lock (_gate)
            _cache[file] = result;
        return result;
    }

    public string? VersionText(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var info = FileVersionInfo.GetVersionInfo(file);
            var text = string.Join(' ', new[] { info.CompanyName, info.ProductName, info.FileDescription, info.Comments, info.OriginalFilename }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return text.Length == 0 ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    public byte[]? ReadHead(string file, int max)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[Math.Min(max, (int)Math.Min(fs.Length, int.MaxValue))];
            var read = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            return read == buf.Length ? buf : buf[..read];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
