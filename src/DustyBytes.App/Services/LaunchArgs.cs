namespace DustyBytes.App.Services;

public enum LaunchMode
{
    Normal,
    Worker,
    Check,
    Inspect,
    Unregister,
}

public sealed record LaunchArgs(LaunchMode Mode, string? Path = null)
{
    public const string Worker = "--worker";
    public const string Check = "--check";
    public const string Inspect = "--inspect";
    public const string Unregister = "--unregister";
    public const string Scheme = "dustybytes";
    public const string OpenUri = Scheme + ":open";

    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new(LaunchMode.Normal);
        var first = args[0];
        if (first == Worker)
            return new(LaunchMode.Worker);
        if (first.Equals(Check, StringComparison.OrdinalIgnoreCase))
            return new(LaunchMode.Check);
        if (first.Equals(Unregister, StringComparison.OrdinalIgnoreCase))
            return new(LaunchMode.Unregister);
        if (first.Equals(Inspect, StringComparison.OrdinalIgnoreCase))
            return args.Count > 1 && Folder(args[1]) is { } path ? new(LaunchMode.Inspect, path) : new(LaunchMode.Normal);
        return new(LaunchMode.Normal);
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
