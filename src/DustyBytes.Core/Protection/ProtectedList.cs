using System.Text.Json;

namespace DustyBytes.Core.Protection;

public enum Badge
{
    None,
    System,
    Cloud,
    Shared,
    Launcher,
    UserData,
    UserException,
    Link,
}

public sealed record Verdict(bool Allowed, string Reason, Badge Badge)
{
    public static readonly Verdict Ok = new(true, "", Badge.None);

    public static Verdict Deny(string reason, Badge badge) => new(false, reason, badge);
}

public sealed class ProtectedList
{
    const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    const FileAttributes CloudMask = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    readonly List<(string Root, string Reason, Badge Badge)> _roots = [];
    readonly Dictionary<string, string> _segments = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _neverLeftover = [];
    readonly List<(string Prefix, string Reason)> _runtimePrefixes = [];
    readonly HashSet<string> _appx = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    public ProtectedList(ProtectedRules rules)
    {
        foreach (var r in rules.Roots)
            AddRoot(Paths.Expand(r.Path), r.Reason, BadgeFor(r.Reason));
        foreach (var s in rules.Segments)
            _segments[s.Name] = s.Reason;
        foreach (var f in rules.Files)
            _files[f.Name] = f.Reason;
        foreach (var c in rules.Cloud)
        {
            var path = c.Env is { } env ? Environment.GetEnvironmentVariable(env) : c.Path is { } p ? Paths.Expand(p) : null;
            if (!string.IsNullOrWhiteSpace(path) && !path.Contains('%'))
                AddRoot(path, c.Reason, Badge.Cloud);
        }
        foreach (var n in rules.NeverLeftover)
        {
            var path = Paths.Expand(n);
            if (!path.Contains('%'))
                _neverLeftover.Add(path);
        }
        foreach (var r in rules.RuntimePrefixes)
            _runtimePrefixes.Add((r.Prefix, r.Reason));
        foreach (var a in rules.Appx)
            _appx.Add(a);
    }

    public static ProtectedList LoadDefault() => new(ProtectedRules.LoadDefault());

    public IReadOnlyList<string> NeverLeftoverRoots => _neverLeftover;

    static Badge BadgeFor(string reason) =>
        reason.Contains("Paylaşılan", StringComparison.OrdinalIgnoreCase) || reason.Contains("çalışma zamanı", StringComparison.OrdinalIgnoreCase)
            ? Badge.Shared
            : Badge.System;

    public void AddRoot(string path, string reason, Badge badge)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('%'))
            return;
        lock (_gate)
            _roots.Add((Paths.Normalize(path), reason, badge));
    }

    public void AddLauncherLibrary(string path, string launcher) =>
        AddRoot(path, $"{launcher} kütüphane kökü, yalnız launcher üzerinden", Badge.Launcher);

    public void AddUserException(string path) =>
        AddRoot(path, "Kullanıcının koruma istisnası", Badge.UserException);

    public void LoadUserExceptions(string file)
    {
        if (!File.Exists(file))
            return;
        using var stream = File.OpenRead(file);
        var list = JsonSerializer.Deserialize(stream, ProtectedRulesJson.Default.ListString) ?? [];
        foreach (var p in list)
            AddUserException(p);
    }

    public bool IsProtectedAppx(string packageFamilyOrName) =>
        _appx.Any(a => packageFamilyOrName.StartsWith(a, StringComparison.OrdinalIgnoreCase));

    public string? RuntimeReason(string displayName)
    {
        foreach (var (prefix, reason) in _runtimePrefixes)
            if (displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return reason;
        return null;
    }

    public string? ProtectedNameReason(string name) =>
        _segments.TryGetValue(name, out var s) ? s : _files.TryGetValue(name, out var f) ? f : null;

    public bool IsNeverLeftover(string path) =>
        _neverLeftover.Any(root => Paths.IsUnder(path, root) || Paths.IsUnder(root, path));

    public Verdict CheckPath(string path) => CheckPath(path, false);

    public Verdict CheckGamePath(string path) => CheckPath(path, true);

    Verdict CheckPath(string path, bool insideLibrary)
    {
        var normalized = Paths.Normalize(path);
        if (normalized.Length <= 3)
            return Verdict.Deny("Sürücü kökü silinemez", Badge.System);

        lock (_gate)
        {
            foreach (var (root, reason, badge) in _roots)
            {
                if (Paths.IsUnder(root, normalized))
                    return Verdict.Deny(reason, badge);
                if (Paths.IsUnder(normalized, root) && !(insideLibrary && badge == Badge.Launcher))
                    return Verdict.Deny(reason, badge);
            }
        }

        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            if (_segments.TryGetValue(part, out var reason))
                return Verdict.Deny(reason, Badge.Shared);

        if (_files.TryGetValue(parts[^1], out var fileReason))
            return Verdict.Deny(fileReason, Badge.System);

        return Verdict.Ok;
    }

    public Func<string, FileAttributes?> AttributeProvider { get; set; } = DefaultAttributes;

    public Verdict Check(string path)
    {
        var byPath = CheckPath(path);
        if (!byPath.Allowed)
            return byPath;
        return CheckFileSystem(path, AttributeProvider);
    }

    public static FileAttributes? DefaultAttributes(string path)
    {
        try
        {
            return File.GetAttributes(Paths.ToLong(path));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static Verdict CheckFileSystem(string path) => CheckFileSystem(path, DefaultAttributes);

    public static Verdict CheckFileSystem(string path, Func<string, FileAttributes?> attributes)
    {
        var normalized = Paths.Normalize(path);
        var current = normalized;
        var isTarget = true;
        while (!string.IsNullOrEmpty(current) && current.Length > 3)
        {
            if (attributes(current) is not { } attrs)
            {
                current = Path.GetDirectoryName(current);
                isTarget = false;
                continue;
            }

            if ((attrs & CloudMask) != 0)
                return Verdict.Deny("Bulut yer tutucusu, diskte yer tutmuyor; silmek buluttan da siler", Badge.Cloud);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return isTarget
                    ? Verdict.Deny("Bağlantı noktası: hedefine girilmez", Badge.Link)
                    : Verdict.Deny("Yol bir bağlantı noktasının içinden geçiyor", Badge.Link);

            current = Path.GetDirectoryName(current);
            isTarget = false;
        }
        return Verdict.Ok;
    }
}
