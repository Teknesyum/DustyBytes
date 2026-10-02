using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DustyBytes.Core.Model;

namespace DustyBytes.Core.Ipc;

public sealed record PreviewFile(string Path, long Bytes, DateTime LastWriteUtc, string Option);

public sealed record OptionTotal(string Option, int Count, long Bytes);

public sealed record CleanPreview
{
    public const int HeadLimit = 200;
    public const int HeadChars = 120_000;

    public string? Id { get; init; }
    public string Digest { get; init; } = "";
    public int Count { get; init; }
    public long Bytes { get; init; }
    public int Protected { get; init; }
    public List<OptionTotal> Options { get; init; } = [];
    public List<PreviewFile> Head { get; init; } = [];

    [JsonIgnore]
    public bool FromWorker => !string.IsNullOrEmpty(Id);

    public bool SameAs(CleanPreview other) =>
        Count == other.Count && string.Equals(Digest, other.Digest, StringComparison.OrdinalIgnoreCase);

    public static CleanPreview Of(IEnumerable<PreviewFile> files, int protectedCount = 0)
    {
        var builder = new PreviewBuilder { Protected = protectedCount };
        foreach (var file in files)
            builder.Add(file.Path, file.Bytes, file.LastWriteUtc, file.Option);
        return builder.Build().Preview;
    }
}

public sealed record BuiltPreview(CleanPreview Preview, ShownList Shown);

public sealed class PreviewBuilder
{
    readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    readonly List<PreviewFile> _head = [];
    readonly Dictionary<string, (int Count, long Bytes)> _options = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _order = [];
    long _bytes;
    int _headChars;
    bool _headFull;

    public int Protected { get; set; }
    public int Count => _seen.Count;

    public bool Add(string path, long bytes, DateTime lastWriteUtc, string option)
    {
        if (!_seen.Add(path))
            return false;
        _bytes += bytes;
        if (!_options.TryGetValue(option, out var total))
            _order.Add(option);
        _options[option] = (total.Count + 1, total.Bytes + bytes);
        if (!_headFull)
        {
            if (_head.Count < CleanPreview.HeadLimit && _headChars + path.Length <= CleanPreview.HeadChars)
            {
                _head.Add(new PreviewFile(path, bytes, lastWriteUtc, option));
                _headChars += path.Length;
            }
            else
            {
                _headFull = true;
            }
        }
        return true;
    }

    public BuiltPreview Build()
    {
        var shown = ShownList.Of(_seen);
        _seen.Clear();
        _seen.TrimExcess();
        var preview = new CleanPreview
        {
            Digest = shown.Digest,
            Count = shown.Count,
            Bytes = _bytes,
            Protected = Protected,
            Options = [.. _order.Select(o => new OptionTotal(o, _options[o].Count, _options[o].Bytes))],
            Head = [.. _head],
        };
        return new BuiltPreview(preview, shown);
    }
}

public sealed class ShownList : IReadOnlySet<string>
{
    public static readonly Comparison<string> Order = (a, b) =>
    {
        var c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return c != 0 ? c : string.CompareOrdinal(a, b);
    };

    readonly string[] _paths;

    ShownList(string[] sorted)
    {
        _paths = sorted;
        Digest = PreviewDigest.OfSorted(sorted);
    }

    public string Digest { get; }
    public int Count => _paths.Length;
    public string this[int index] => _paths[index];

    public static ShownList Of(IEnumerable<string> paths)
    {
        var array = paths.ToArray();
        Array.Sort(array, Order);
        var unique = 0;
        for (var i = 0; i < array.Length; i++)
            if (unique == 0 || !string.Equals(array[unique - 1], array[i], StringComparison.OrdinalIgnoreCase))
                array[unique++] = array[i];
        if (unique != array.Length)
            Array.Resize(ref array, unique);
        return new ShownList(array);
    }

    public int IndexOf(string path)
    {
        var index = Array.BinarySearch(_paths, path, StringComparer.OrdinalIgnoreCase);
        return index < 0 ? -1 : index;
    }

    public bool Contains(string item) => IndexOf(item) >= 0;

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_paths).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    HashSet<string> Set() => new(_paths, StringComparer.OrdinalIgnoreCase);

    public bool IsProperSubsetOf(IEnumerable<string> other) => Set().IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<string> other) => Set().IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<string> other) => Set().IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<string> other) => other.All(Contains);
    public bool Overlaps(IEnumerable<string> other) => other.Any(Contains);
    public bool SetEquals(IEnumerable<string> other) => Set().SetEquals(other);
}

public static class PreviewDigest
{
    static readonly byte[] Newline = [10];

    public static string Of(IEnumerable<string> paths)
    {
        var array = paths.ToArray();
        Array.Sort(array, ShownList.Order);
        return OfSorted(array);
    }

    public static string OfSorted(IReadOnlyList<string> sorted)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            var used = 0;
            for (var i = 0; i < sorted.Count; i++)
            {
                var path = sorted[i];
                var need = Encoding.UTF8.GetMaxByteCount(path.Length) + 1;
                if (used + need > buffer.Length)
                {
                    hash.AppendData(buffer, 0, used);
                    used = 0;
                }
                if (need > buffer.Length)
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(path));
                    hash.AppendData(Newline);
                    continue;
                }
                used += Encoding.UTF8.GetBytes(path, buffer.AsSpan(used));
                buffer[used++] = Newline[0];
            }
            hash.AppendData(buffer, 0, used);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

public sealed record PathTally
{
    public int Shown { get; init; }
    public int Processed { get; init; }
    public long ProcessedBytes { get; init; }
    public int Vanished { get; init; }
    public int Protected { get; init; }
    public int Locked { get; init; }
    public int Failed { get; init; }
    public int NotShown { get; init; }
    public bool Stopped { get; init; }

    [JsonIgnore]
    public int Skipped => Vanished + Protected + Locked + Failed;

    [JsonIgnore]
    public bool Balanced => Shown == Processed + Skipped;

    public string Reasons()
    {
        var parts = new List<(int Count, string Text)>
        {
            (Locked, "kullanımda"),
            (Vanished, "kayboldu"),
            (Protected, "korumalı"),
            (Failed, "silinemedi"),
            (NotShown, "gösterilmedi"),
        }.Where(p => p.Count > 0).ToList();
        if (parts.Count == 1)
            return parts[0].Text;
        return string.Join(", ", parts.Select(p => $"{Format.Count(p.Count)} {p.Text}"));
    }

    public string Describe(string noun = "dosya", string verb = "silinen")
    {
        var text = $"Gösterilen {Format.Count(Shown)} {noun}, {verb} {Format.Count(Processed)}";
        if (Skipped > 0)
            text += $", atlanan {Format.Count(Skipped)} ({SkipReasons()})";
        if (NotShown > 0)
            text += $"; önizlemeden sonra çıkan {Format.Count(NotShown)} {noun} gösterilmediği için dokunulmadı";
        return text;
    }

    string SkipReasons() => (this with { NotShown = 0 }).Reasons();

    public PathTally Plus(PathTally other) => new()
    {
        Shown = Shown + other.Shown,
        Processed = Processed + other.Processed,
        ProcessedBytes = ProcessedBytes + other.ProcessedBytes,
        Vanished = Vanished + other.Vanished,
        Protected = Protected + other.Protected,
        Locked = Locked + other.Locked,
        Failed = Failed + other.Failed,
        NotShown = NotShown + other.NotShown,
        Stopped = Stopped || other.Stopped,
    };
}
