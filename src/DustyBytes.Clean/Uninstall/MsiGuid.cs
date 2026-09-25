using System.Text;

namespace DustyBytes.Clean.Uninstall;

public static class MsiGuid
{
    static readonly int[] GroupLengths = [8, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2];

    public static bool TryParseBraced(string? text, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim();
        return t.Length == 38 && t[0] == '{' && t[^1] == '}' && Guid.TryParseExact(t, "B", out guid);
    }

    public static string Format(Guid guid) => guid.ToString("B").ToUpperInvariant();

    public static string Compress(Guid guid)
    {
        var hex = guid.ToString("N").ToUpperInvariant();
        return Swap(hex);
    }

    public static bool TryExpand(string? packed, out Guid guid)
    {
        guid = Guid.Empty;
        if (packed is null || packed.Length != 32 || !packed.All(Uri.IsHexDigit))
            return false;
        return Guid.TryParseExact(Swap(packed.ToUpperInvariant()), "N", out guid);
    }

    public static string? Expand(string? packed) => TryExpand(packed, out var g) ? Format(g) : null;

    static string Swap(string hex)
    {
        var sb = new StringBuilder(32);
        var pos = 0;
        foreach (var len in GroupLengths)
        {
            for (var i = len - 1; i >= 0; i--)
                sb.Append(hex[pos + i]);
            pos += len;
        }
        return sb.ToString();
    }
}
