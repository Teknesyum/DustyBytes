using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public sealed record DriveDelta(string Root, long Before, long After)
{
    public long Freed => After - Before;
    public string Label => Root.TrimEnd('\\', '/');
    public string Text => $"{Label} önce {Format.Bytes(Before)} boş, sonra {Format.Bytes(After)} boş";
}

public sealed class SpaceMeter
{
    readonly IAppBackend _backend;
    readonly Dictionary<string, (string Root, long Free)> _before;
    readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);

    SpaceMeter(IAppBackend backend)
    {
        _backend = backend;
        _before = Read(backend);
    }

    public static SpaceMeter Start(IAppBackend backend) => new(backend);

    static string Key(string root) => root.TrimEnd('\\', '/').ToUpperInvariant();

    static Dictionary<string, (string Root, long Free)> Read(IAppBackend backend)
    {
        var map = new Dictionary<string, (string Root, long Free)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in backend.Drives())
                if (!string.IsNullOrEmpty(drive.Root) && drive.FreeBytes >= 0)
                    map[Key(drive.Root)] = (drive.Root, drive.FreeBytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
        return map;
    }

    public void Touch(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (Path.GetPathRoot(path) is { Length: > 0 } root)
                _touched.Add(Key(root));
        }
        catch (ArgumentException)
        {
        }
    }

    public void Touch(Unit unit)
    {
        foreach (var path in unit.Paths)
            Touch(path);
    }

    public IReadOnlyList<DriveDelta> Finish()
    {
        var after = Read(_backend);
        var keys = _touched.Where(k => _before.ContainsKey(k) && after.ContainsKey(k)).ToList();
        if (keys.Count == 0)
            keys = [.. _before.Keys.Where(k => after.TryGetValue(k, out var a) && a.Free != _before[k].Free)];
        return [.. keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Select(k => new DriveDelta(_before[k].Root, _before[k].Free, after[k].Free))];
    }

    public static long Total(IReadOnlyList<DriveDelta> deltas) => Math.Max(0, deltas.Sum(d => d.Freed));

    public static string Describe(IReadOnlyList<DriveDelta> deltas) => string.Join(" · ", deltas.Select(d => d.Text));
}
