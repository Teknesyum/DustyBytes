using DustyBytes.Core;
using DustyBytes.Core.Protection;

namespace DustyBytes.Clean.Safety;

public sealed class SafetyGate
{
    readonly List<string> _userData = [];
    readonly object _lock = new();

    public SafetyGate(ProtectedList list)
    {
        List = list;
    }

    public static SafetyGate LoadDefault() => new(ProtectedList.LoadDefault());

    public ProtectedList List { get; }

    public IReadOnlyList<string> UserDataRoots
    {
        get
        {
            lock (_lock)
                return [.. _userData];
        }
    }

    public void AddUserDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var expanded = Paths.Expand(path);
        if (expanded.Contains('%') || !Path.IsPathFullyQualified(expanded))
            return;
        lock (_lock)
            _userData.Add(Paths.Normalize(expanded));
    }

    public bool IsUserData(string path)
    {
        if (List.IsNeverLeftover(path))
            return true;
        lock (_lock)
            return _userData.Any(root => Paths.IsUnder(path, root) || Paths.IsUnder(root, path));
    }

    public Verdict Check(string path, bool includeUserData)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Verdict.Deny("Yol boş", Badge.System);

        string normalized;
        try
        {
            var plain = Paths.FromLong(path);
            if (!Path.IsPathFullyQualified(plain))
                return Verdict.Deny("Yol tam değil; göreli yol kabul edilmez", Badge.System);
            normalized = Paths.Normalize(plain);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Verdict.Deny("Geçersiz yol", Badge.System);
        }

        var verdict = List.Check(normalized);
        if (!verdict.Allowed)
            return verdict;

        foreach (var part in normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (part.Equals(Paths.QuarantineDir, StringComparison.OrdinalIgnoreCase))
                return Verdict.Deny("Karantina alanı; yalnız geri yükleme ya da kalıcı silme ile", Badge.System);

        if (!includeUserData && IsUserData(normalized))
            return Verdict.Deny("Kullanıcı verisi: ayrı açık onay gerekir", Badge.UserData);

        return Verdict.Ok;
    }
}
