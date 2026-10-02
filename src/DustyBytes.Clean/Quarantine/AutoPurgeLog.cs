using System.Globalization;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;

namespace DustyBytes.Clean.Quarantine;

public sealed record AutoPurgeRecord(DateTimeOffset At, long Bytes, int Count, string Root);

public static class AutoPurgeLog
{
    public static string DefaultPath => Path.Combine(Paths.AppData, "auto-purge.log");

    public static IReadOnlyList<AutoPurgeRecord> Summarize(IEnumerable<OpResult> results, DateTimeOffset at)
    {
        var groups = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result.Status is OpStatus.DryRun or OpStatus.Denied or OpStatus.NotFound or OpStatus.Conflict || result.Bytes <= 0)
                continue;
            if (result.Status is not (OpStatus.Done or OpStatus.Locked or OpStatus.Failed))
                continue;
            var root = RootOf(result.Path);
            groups.TryGetValue(root, out var sum);
            groups[root] = (sum.Bytes + result.Bytes, sum.Count + (result.Status == OpStatus.Done ? 1 : 0));
        }
        return [.. groups.Select(g => new AutoPurgeRecord(at, g.Value.Bytes, g.Value.Count, g.Key))];
    }

    static string RootOf(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root ? root : "";
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    public static void Append(IReadOnlyList<AutoPurgeRecord> records, string? path = null)
    {
        if (records.Count == 0)
            return;
        var target = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.AppendAllLines(target, records.Select(Line));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static string Line(AutoPurgeRecord r) =>
        string.Join('\t',
            r.At.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            r.Bytes.ToString(CultureInfo.InvariantCulture),
            r.Count.ToString(CultureInfo.InvariantCulture),
            r.Root);

    public static IReadOnlyList<AutoPurgeRecord> Take(string? path = null)
    {
        var target = path ?? DefaultPath;
        if (!File.Exists(target))
            return [];
        var claimed = target + "." + Guid.NewGuid().ToString("N") + ".taken";
        try
        {
            File.Move(target, claimed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var records = new List<AutoPurgeRecord>();
        try
        {
            foreach (var line in File.ReadAllLines(claimed))
                if (Parse(line) is { } record)
                    records.Add(record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        try
        {
            File.Delete(claimed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return records;
    }

    static AutoPurgeRecord? Parse(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 4
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || bytes <= 0)
            return null;
        try
        {
            return new AutoPurgeRecord(DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime(), bytes, count, parts[3]);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
