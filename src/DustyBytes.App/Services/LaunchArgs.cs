namespace DustyBytes.App.Services;

public enum LaunchMode
{
    Normal,
    Worker,
    Check,
    Inspect,
    Unregister,
    SafeClean,
    Snooze,
    Mute,
}

public sealed record LaunchArgs(LaunchMode Mode, string? Path = null, string? Token = null)
{
    public const string Worker = "--worker";
    public const string Check = "--check";
    public const string Inspect = "--inspect";
    public const string Unregister = "--unregister";
    public const string Scheme = "dustybytes";
    public const string SafeClean = "--safe-clean";
    public const string OpenUri = Scheme + ":open";
    public const string SafeCleanAction = "safe-clean";
    public const string SnoozeAction = "snooze";
    public const string MuteAction = "mute";

    public static string ActionUri(string action, string token) => $"{Scheme}:{action}?t={token}";

    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new(LaunchMode.Normal);
        var first = args[0].Trim().Trim('"');
        if (first == Worker)
            return new(LaunchMode.Worker);
        if (first.Equals(Check, StringComparison.OrdinalIgnoreCase))
            return new(LaunchMode.Check);
        if (first.Equals(Unregister, StringComparison.OrdinalIgnoreCase))
            return new(LaunchMode.Unregister);
        if (first.Equals(SafeClean, StringComparison.OrdinalIgnoreCase))
            return new(LaunchMode.SafeClean);
        if (first.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
            return FromUri(first[(Scheme.Length + 1)..]);
        if (first.Equals(Inspect, StringComparison.OrdinalIgnoreCase))
            return args.Count > 1 && Folder(args[1]) is { } path ? new(LaunchMode.Inspect, path) : new(LaunchMode.Normal);
        return new(LaunchMode.Normal);
    }

    static LaunchArgs FromUri(string rest)
    {
        var cut = rest.IndexOf('?');
        var action = (cut < 0 ? rest : rest[..cut]).Trim('/').ToLowerInvariant();
        var token = cut < 0 ? null : TokenOf(rest[(cut + 1)..]);
        var mode = action switch
        {
            SafeCleanAction => LaunchMode.SafeClean,
            SnoozeAction => LaunchMode.Snooze,
            MuteAction => LaunchMode.Mute,
            _ => LaunchMode.Normal,
        };
        return mode == LaunchMode.Normal || token is null ? new(LaunchMode.Normal) : new(mode, null, token);
    }

    static string? TokenOf(string query)
    {
        foreach (var part in query.TrimEnd('/').Split('&'))
        {
            if (part.Length == 34 && part.StartsWith("t=", StringComparison.Ordinal) && IsHex(part.AsSpan(2)))
                return part[2..];
        }
        return null;
    }

    static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (!char.IsAsciiHexDigitLower(c))
                return false;
        return true;
    }

    public static string? Folder(string raw)
    {
        var text = raw.Trim().Trim('"').Trim();
        if (text.Length == 0 || text.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            return null;
        if (text.Length == 2 && text[1] == ':')
            text += "\\";
        if (!System.IO.Path.IsPathFullyQualified(text))
            return null;
        try
        {
            var full = System.IO.Path.GetFullPath(text);
            return full.Length > 3 ? System.IO.Path.TrimEndingDirectorySeparator(full) : full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
