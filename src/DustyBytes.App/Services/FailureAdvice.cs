using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public sealed record FailureNote(string Name, string Why, string Advice)
{
    public string Text => $"{Name}: {Why}. {Advice}";
}

public static class FailureAdvice
{
    const string HolderMark = " — Tutan: ";
    static readonly string[] Statuses = ["Done", "DryRun", "Denied", "Conflict", "Locked", "NotFound", "Failed", "Scheduled"];

    public static FailureNote? For(string name, string message)
    {
        var (status, rest) = Split(message ?? "");
        var (reason, holders) = Holders(rest);
        return status switch
        {
            "NotFound" or "Done" or "DryRun" or "Scheduled" => null,
            "Locked" => new(name, "Dosya kullanımda", holders is { } h ? $"{h} kapatıp yeniden deneyin" : "İlgili programı kapatıp yeniden deneyin"),
            "Denied" => new(name, reason.Length > 0 ? $"Korumalı ({reason})" : "Korumalı", "Bir şey yapmanız gerekmez; DustyBytes buna dokunmaz"),
            "Conflict" => new(name, "Aynı adla bir öğe zaten var", "Karantina ekranından bakıp yeniden deneyin"),
            "Failed" => new(name, holders is null ? "Erişilemedi" : "Dosya kullanımda", holders is { } fh ? $"{fh} kapatıp yeniden deneyin" : "Bilgisayarı yeniden başlatıp yeniden deneyin"),
            _ => Plain(name, reason, holders),
        };
    }

    static FailureNote Plain(string name, string reason, string? holders)
    {
        if (holders is { } h)
            return new(name, "Dosya kullanımda", $"{h} kapatıp yeniden deneyin");
        var why = reason.Length > 0 ? reason.TrimEnd('.', ' ') : "Tamamlanamadı";
        var advice = why.Contains("çalışıyor", StringComparison.OrdinalIgnoreCase) || why.Contains("açık", StringComparison.OrdinalIgnoreCase)
            ? "Programı kapatıp yeniden deneyin"
            : "Temizlik ekranından yeniden deneyin";
        return new(name, why, advice);
    }

    public static IReadOnlyList<FailureNote> ForTally(PathTally? tally, string name = "Önbellek ve geçici dosyalar")
    {
        if (tally is null)
            return [];
        var notes = new List<FailureNote>();
        if (tally.Locked > 0)
            notes.Add(new(name, $"{Format.Count(tally.Locked)} dosya kullanımda", "Açık programları (örneğin tarayıcıyı) kapatıp yeniden deneyin"));
        if (tally.Failed > 0)
            notes.Add(new(name, $"{Format.Count(tally.Failed)} dosya silinemedi", "Bilgisayarı yeniden başlatıp yeniden deneyin"));
        return notes;
    }

    static (string? Status, string Text) Split(string message)
    {
        var colon = message.IndexOf(':');
        var head = colon < 0 ? message.Trim() : message[..colon].Trim();
        if (Statuses.Contains(head, StringComparer.Ordinal))
            return (head, colon < 0 ? "" : message[(colon + 1)..].Trim());
        return (null, message.Trim());
    }

    static (string Reason, string? Holders) Holders(string rest)
    {
        var at = rest.IndexOf(HolderMark, StringComparison.Ordinal);
        if (at < 0)
            return (rest, null);
        var names = rest[(at + HolderMark.Length)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.IndexOf(" (", StringComparison.Ordinal) is var p and > 0 ? h[..p] : h)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (rest[..at].Trim(), names.Count == 0 ? null : string.Join(", ", names) + (names.Count == 1 ? " programını" : " programlarını"));
    }
}
