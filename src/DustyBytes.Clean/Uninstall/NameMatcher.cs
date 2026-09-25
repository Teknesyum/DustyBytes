using System.Text;
using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Uninstall;

public enum NameMatch
{
    None,
    Partial,
    Exact,
}

public static partial class NameMatcher
{
    public static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "tools", "tool", "update", "updater", "updates", "client", "software", "microsoft", "app", "apps",
        "application", "applications", "service", "services", "program", "programs", "data", "files", "file",
        "common", "shared", "system", "windows", "win", "setup", "install", "installer", "uninstall", "launcher",
        "helper", "manager", "x64", "x86", "amd64", "arm64", "bit", "inc", "ltd", "llc", "corp", "corporation",
        "company", "co", "gmbh", "limited", "the", "and", "for", "of", "version", "edition", "free", "pro",
        "beta", "driver", "drivers", "runtime", "plugin", "plugins", "cache", "temp", "tmp", "logs", "log",
        "config", "settings", "user", "users", "local", "roaming", "desktop", "studio", "package", "packages",
        "components", "component", "library", "framework", "support", "web", "online", "player", "games",
        "game", "suite", "team", "technologies", "technology", "systems", "labs", "international", "group",
        "sdk", "lite", "portable", "net", "com", "org", "en", "us", "tr", "full", "new",
    };

    public static readonly string[] SharedPublishers =
    [
        "microsoft", "adobe", "nvidia", "google", "intel", "amd", "advancedmicrodevices",
    ];

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^v?\d+([._-]\d+)*[a-z]?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionToken();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    public static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var s = Bracketed().Replace(text, " ");
        s = CamelBoundary().Replace(s, " ");
        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '.')
                sb.Append(char.ToLowerInvariant(c));
            else
                Flush();
        }
        Flush();
        return parts;

        void Flush()
        {
            if (sb.Length == 0)
                return;
            var t = sb.ToString().Trim('.');
            sb.Clear();
            if (t.Length == 0 || VersionToken().IsMatch(t))
                return;
            foreach (var piece in t.Split('.', StringSplitOptions.RemoveEmptyEntries))
                if (!VersionToken().IsMatch(piece))
                    parts.Add(piece);
        }
    }

    public static IReadOnlyList<string> SignificantTokens(string? text) =>
        Tokens(text).Where(t => t.Length >= 3 && !GenericTokens.Contains(t)).ToList();

    public static string Compact(string? text) => string.Concat(Tokens(text));

    public static string CompactSignificant(string? text) => string.Concat(SignificantTokens(text));

    public static NameMatch Match(string? candidate, string? product)
    {
        var cSig = SignificantTokens(candidate);
        var pSig = SignificantTokens(product);
        if (cSig.Count == 0 || pSig.Count == 0)
            return NameMatch.None;

        var cAll = Compact(candidate);
        var pAll = Compact(product);
        var cS = string.Concat(cSig);
        var pS = string.Concat(pSig);
        if (cS.Length >= 4 && (cAll == pAll || cS == pS || cAll == pS || cS == pAll))
            return NameMatch.Exact;

        if (cS.Length >= 4 && cSig.All(t => pSig.Contains(t)))
            return NameMatch.Partial;

        if (cS.Length >= 5 && pS.StartsWith(cS, StringComparison.Ordinal) && cS.Length * 2 >= pS.Length)
            return NameMatch.Partial;

        return NameMatch.None;
    }

    static readonly HashSet<string> LegalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "inc", "ltd", "llc", "corp", "corporation", "company", "co", "gmbh", "limited", "the", "sa", "ag",
        "ab", "bv", "srl", "sro", "oy", "as", "plc", "pty", "kg", "software", "technologies", "technology",
    };

    static IReadOnlyList<string> PublisherTokens(string? publisher) =>
        Tokens(publisher).Where(t => !LegalTokens.Contains(t)).ToList();

    public static string PublisherCore(string? publisher)
    {
        var t = PublisherTokens(publisher);
        return t.Count == 0 ? "" : t[0];
    }

    public static bool MatchesPublisher(string? candidate, string? publisher)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(publisher))
            return false;
        var c = string.Concat(PublisherTokens(candidate));
        if (c.Length < 3)
            return false;
        var p = string.Concat(PublisherTokens(publisher));
        if (c == p)
            return true;
        var core = PublisherCore(publisher);
        return core.Length >= 3 && c == core;
    }

    public static bool IsSharedPublisher(string? publisherOrFolder)
    {
        if (string.IsNullOrWhiteSpace(publisherOrFolder))
            return false;
        var compact = Compact(publisherOrFolder);
        var tokens = Tokens(publisherOrFolder);
        foreach (var s in SharedPublishers)
            if (compact == s || compact.StartsWith(s + "corporation", StringComparison.Ordinal) || tokens.Count > 0 && tokens[0] == s || compact == "advancedmicrodevicesinc")
                return true;
        return false;
    }
}
