using System.Security.Cryptography;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Clean.Rules;

public sealed class PreviewStore(Func<DateTime>? clock = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int Capacity = 3;
    public const long PathBudget = 4_000_000;

    sealed record Entry(string Id, string Digest, string Selection, ShownList Shown, DateTime Created);

    readonly Lock _lock = new();
    readonly List<Entry> _entries = [];

    DateTime Now() => clock?.Invoke() ?? DateTime.UtcNow;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                Prune(Now());
                return _entries.Count;
            }
        }
    }

    static string SelectionKey(IEnumerable<string> optionKeys) =>
        string.Join("\n", optionKeys.Select(k => k.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    public CleanPreview Add(BuiltPreview built, IEnumerable<string> optionKeys)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var entry = new Entry(id, built.Preview.Digest, SelectionKey(optionKeys), built.Shown, Now());
        lock (_lock)
        {
            Prune(entry.Created);
            _entries.Add(entry);
            while (_entries.Count > Capacity || _entries.Count > 1 && _entries.Sum(e => (long)e.Shown.Count) > PathBudget)
                _entries.RemoveAt(0);
        }
        return built.Preview with { Id = id };
    }

    public ShownList? Take(string? id, string? digest, IEnumerable<string> optionKeys)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(digest))
            return null;
        Entry? entry;
        lock (_lock)
        {
            Prune(Now());
            entry = _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (entry is null)
                return null;
            _entries.Remove(entry);
        }
        if (!string.Equals(entry.Digest, digest, StringComparison.OrdinalIgnoreCase) || entry.Shown.Digest != entry.Digest)
            return null;
        return entry.Selection == SelectionKey(optionKeys) ? entry.Shown : null;
    }

    void Prune(DateTime now) => _entries.RemoveAll(e => now - e.Created > Lifetime || now < e.Created);
}
