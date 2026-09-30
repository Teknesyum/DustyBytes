using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;

namespace DustyBytes.Scan.Duplicates;

public sealed record HashEntry(long Length, long Written, string? Edge, string? Full);

public sealed class HashCache
{
    readonly Dictionary<string, HashEntry> _old;
    readonly Dictionary<string, HashEntry> _used = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    public HashCache(string? path = null)
    {
        Path = path;
        _old = path is null ? new(StringComparer.OrdinalIgnoreCase) : Load(path);
    }

    public string? Path { get; }

    public static string DefaultPath => System.IO.Path.Combine(Paths.AppData, "duplicates.json");

    public int Count
    {
        get
        {
            lock (_lock)
                return _used.Count;
        }
    }

    static Dictionary<string, HashEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllText(path), HashCacheJson.Default.DictionaryStringHashEntry) is { } found)
                return new(found, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public HashEntry? Get(FileFacts facts)
    {
        lock (_lock)
        {
            if (_used.TryGetValue(facts.Path, out var entry) && Matches(entry, facts))
                return entry;
            if (_old.TryGetValue(facts.Path, out entry) && Matches(entry, facts))
                return _used[facts.Path] = entry;
            return null;
        }
    }

    static bool Matches(HashEntry entry, FileFacts facts) =>
        entry.Length == facts.Length && entry.Written == facts.Written.UtcTicks;

    public void Put(FileFacts facts, string? edge, string? full)
    {
        lock (_lock)
        {
            var known = Get(facts);
            _used[facts.Path] = new HashEntry(facts.Length, facts.Written.UtcTicks, edge ?? known?.Edge, full ?? known?.Full);
        }
    }

    public void Save()
    {
        if (Path is null)
            return;
        Dictionary<string, HashEntry> snapshot;
        lock (_lock)
            snapshot = new(_used, StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, HashCacheJson.Default.DictionaryStringHashEntry));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, HashEntry>))]
public partial class HashCacheJson : JsonSerializerContext;
