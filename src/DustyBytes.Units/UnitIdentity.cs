using System.Security.Cryptography;
using System.Text;
using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public static class UnitIdentity
{
    public static string Compute(UnitKind kind, string primaryPath)
    {
        var normalized = Paths.Normalize(primaryPath).ToUpperInvariant();
        var bytes = Encoding.UTF8.GetBytes(kind + "|" + normalized);
        var hash = SHA256.HashData(bytes);
        return $"{kind}-{Convert.ToHexString(hash)[..16]}";
    }
}
