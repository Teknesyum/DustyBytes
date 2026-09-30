using DustyBytes.Core;

namespace DustyBytes.Scan.Duplicates;

public sealed class DuplicateGuard(string keep)
{
    string? _keepHash;
    FileFacts? _keepFacts;

    public string Keep { get; } = keep;

    public string? Check(string candidate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Keep))
            return "Kalacak kopya belirtilmedi";
        string keepPath, path;
        try
        {
            keepPath = Paths.Normalize(Keep);
            path = Paths.Normalize(candidate);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Geçersiz yol";
        }
        if (keepPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            return "Kalacak kopya karantinaya alınmaz";

        var keepNow = FileProbe.Probe(keepPath);
        if (Refusal(keepNow, "Kalacak kopya") is { } keepReason)
            return keepReason;
        if (_keepFacts is { } before && (before.Key != keepNow.Key || before.Length != keepNow.Length || before.Written != keepNow.Written))
            _keepHash = null;
        _keepFacts = keepNow;

        var other = FileProbe.Probe(path);
        if (Refusal(other, "Dosya") is { } reason)
            return reason;
        if (other.Key == keepNow.Key)
            return "Aynı dosyanın ikinci adı (sabit bağlantı); kopya değil";
        if (other.Links > 1)
            return "Sabit bağlantılı dosya; taşımak yer açmaz";
        if (other.Length != keepNow.Length)
            return "Kalacak kopyayla boyutu artık aynı değil";

        try
        {
            _keepHash ??= Hash(keepPath, keepNow, ct);
            if (_keepHash is null)
                return "Kalacak kopya okunamadı";
            var hash = Hash(path, other, ct);
            if (hash is null)
                return "Dosya okunamadı";
            if (!hash.Equals(_keepHash, StringComparison.Ordinal))
                return "Kalacak kopyayla içeriği artık aynı değil";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Dosya okunamadı: " + e.Message;
        }
        return null;
    }

    static string? Refusal(FileFacts facts, string who) => facts.State switch
    {
        FileState.Ready => null,
        FileState.Missing => who + " bulunamadı",
        FileState.Cloud => who + " bulutta, yerel kopyası yok; karşılaştırılamaz",
        FileState.Link => who + " bir bağlantı; karşılaştırılamaz",
        _ => who + " okunamadı",
    };

    static string? Hash(string path, FileFacts expected, CancellationToken ct)
    {
        using var stream = FileProbe.Open(path);
        var now = FileProbe.Describe(path, stream.SafeFileHandle);
        if (!now.Ready || now.Key != expected.Key || now.Length != expected.Length)
            return null;
        return ContentHash.Full(stream, ct);
    }
}
