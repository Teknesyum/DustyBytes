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

    readonly List<(string Root, string Reason, Badge Badge, string? Via)> _roots = [];
    readonly Dictionary<string, string> _segments = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(string[] Parts, string Reason)> _driveRoots = [];
    readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _via = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _systemOps = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _neverLeftover = [];
    readonly List<(string Prefix, string Reason)> _runtimePrefixes = [];
    readonly HashSet<string> _appx = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    public ProtectedList(ProtectedRules rules)
    {
        foreach (var r in rules.Roots)
            AddRoot(Paths.Expand(r.Path), r.Reason, BadgeFor(r.Reason), Clean(r.Via));
        foreach (var s in rules.Segments)
        {
            _segments[s.Name] = s.Reason;
            if (Clean(s.Via) is { } via)
                _via["segment:" + s.Name] = via;
        }
        foreach (var f in rules.Files)
        {
            _files[f.Name] = f.Reason;
            if (Clean(f.Via) is { } via)
                _via["file:" + f.Name] = via;
        }
        foreach (var d in rules.DriveRoots)
            if (d.Name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } driveParts)
                _driveRoots.Add((driveParts, d.Reason));
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

    string? Clean(string? via)
    {
        if (string.IsNullOrWhiteSpace(via))
            return null;
        var trimmed = via.Trim();
        _systemOps.Add(trimmed);
        return trimmed;
    }

    public bool SystemOpAllowed(string op) => _systemOps.Contains(op);

    public IReadOnlyList<string> NeverLeftoverRoots => _neverLeftover;

    static Badge BadgeFor(string reason) =>
        reason.Contains("Paylaşılan", StringComparison.OrdinalIgnoreCase) || reason.Contains("çalışma zamanı", StringComparison.OrdinalIgnoreCase)
            ? Badge.Shared
            : Badge.System;

    public void AddRoot(string path, string reason, Badge badge) => AddRoot(path, reason, badge, null);

    void AddRoot(string path, string reason, Badge badge, string? via)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('%'))
            return;
        lock (_gate)
            _roots.Add((Paths.Normalize(path), reason, badge, via));
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

    public Verdict CheckPath(string path) => CheckPath(path, false, null);

    public Verdict CheckGamePath(string path) => CheckPath(path, true, null);

    public Verdict CheckPathVia(string path, string via) => CheckPath(path, false, via);

    public Verdict CheckVia(string path, string via)
    {
        var byPath = CheckPathVia(path, via);
        if (!byPath.Allowed)
            return byPath;
        return CheckFileSystem(path, AttributeProvider);
    }

    bool Waived(string key, string? via) =>
        via is not null && _via.TryGetValue(key, out var ruleVia) && ruleVia.Equals(via, StringComparison.OrdinalIgnoreCase);

    Verdict CheckPath(string path, bool insideLibrary, string? via)
    {
        var normalized = Paths.Normalize(path);
        if (normalized.Length <= 3)
            return Verdict.Deny("Sürücü kökü silinemez", Badge.System);

        lock (_gate)
        {
            foreach (var (root, reason, badge, ruleVia) in _roots)
            {
                if (via is not null && via.Equals(ruleVia, StringComparison.OrdinalIgnoreCase) && !Paths.IsUnder(root, normalized) && Paths.IsUnder(normalized, root))
                    continue;
                if (Paths.IsUnder(root, normalized))
                    return Verdict.Deny(reason, badge);
                if (Paths.IsUnder(normalized, root) && !(insideLibrary && badge == Badge.Launcher))
                    return Verdict.Deny(reason, badge);
            }
        }

        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (DriveRootReason(parts) is { } driveReason)
            return Verdict.Deny(driveReason, Badge.System);
        foreach (var part in parts)
            if (_segments.TryGetValue(part, out var reason) && !Waived("segment:" + part, via))
                return Verdict.Deny(reason, Badge.Shared);

        if (_files.TryGetValue(parts[^1], out var fileReason) && !Waived("file:" + parts[^1], via))
            return Verdict.Deny(fileReason, Badge.System);

        return Verdict.Ok;
    }

    string? DriveRootReason(string[] parts)
    {
        if (parts.Length < 2 || parts[0].Length != 2 || parts[0][1] != ':')
            return null;
        foreach (var (rule, reason) in _driveRoots)
        {
            var shared = Math.Min(rule.Length, parts.Length - 1);
            var match = true;
            for (var i = 0; i < shared && match; i++)
                match = rule[i].Equals(parts[i + 1], StringComparison.OrdinalIgnoreCase);
            if (match)
                return reason;
        }
        return null;
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
