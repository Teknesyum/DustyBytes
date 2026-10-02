using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DustyBytes.Core.Model;

namespace DustyBytes.Core.Ipc;

public sealed record PreviewFile(string Path, long Bytes, DateTime LastWriteUtc, string Option);

public sealed record CleanPreview
{
    public List<PreviewFile> Files { get; init; } = [];
    public long Bytes { get; init; }
    public string Digest { get; init; } = "";
    public int Protected { get; init; }

    [JsonIgnore]
    public int Count => Files.Count;

    public List<string> Paths() => [.. Files.Select(f => f.Path)];

    public static CleanPreview Of(IEnumerable<PreviewFile> files, int protectedCount = 0)
    {
        List<PreviewFile> list = [.. files];
        return new CleanPreview
        {
            Files = list,
            Bytes = list.Sum(f => f.Bytes),
            Digest = PreviewDigest.Of(list.Select(f => f.Path)),
            Protected = protectedCount,
        };
    }
}

public static class PreviewDigest
{
    public static string Of(IEnumerable<string> paths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData("\n"u8);
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
}
