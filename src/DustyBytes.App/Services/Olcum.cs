using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;

namespace DustyBytes.App.Services;

public sealed record OlcumEvent(
    [property: JsonPropertyName("t")] DateTimeOffset At,
    [property: JsonPropertyName("s")] string Session,
    [property: JsonPropertyName("e")] string Kind,
    [property: JsonPropertyName("v")] long? Value = null,
    [property: JsonPropertyName("d")] string? Detail = null);

public sealed record OlcumSummary(
    string Session,
    DateTimeOffset Started,
    TimeSpan? ToFirstCard,
    TimeSpan? ToFirstFreed,
    long FirstFreedBytes,
    int Decisions,
    int Clicks,
    int Undos);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OlcumEvent))]
public partial class OlcumJson : JsonSerializerContext;

public static class OlcumKind
{
    public const string Start = "start";
    public const string FirstCard = "first-card";
    public const string ScanDone = "scan-done";
    public const string FirstFreed = "first-freed";
    public const string Decision = "decision";
    public const string Click = "click";
    public const string Undo = "undo";
}

public sealed class Olcum
{
    public const long MaxBytes = 1L << 20;

    readonly Lock _lock = new();
    readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    readonly Func<DateTimeOffset> _clock;

    public Olcum(string? path = null, Func<DateTimeOffset>? clock = null, string? session = null)
    {
        Path = path ?? DefaultPath;
        Session = session ?? Guid.NewGuid().ToString("N");
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    static readonly AsyncLocal<Olcum?> Scoped = new();
    static Olcum? _global;

    public static Olcum? Current
    {
        get => Scoped.Value ?? _global;
        set => _global = value;
    }

    public static IDisposable Use(Olcum olcum)
    {
        var before = Scoped.Value;
        Scoped.Value = olcum;
        return new Restore(() => Scoped.Value = before);
    }

    sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    public static string DefaultPath => System.IO.Path.Combine(Paths.AppData, "olcum.jsonl");

    public string Path { get; }
    public string Session { get; }

    public static Olcum Begin(string? path = null)
    {
        var olcum = new Olcum(path);
        olcum.Write(OlcumKind.Start);
        return olcum;
    }

    public static void Mark(string kind, long? value = null, string? detail = null) => Current?.Write(kind, value, detail);

    public static void First(string kind, long? value = null) => Current?.Write(kind, value, null, once: true);

    public static void Click(string action, bool decision = false)
    {
        Current?.Write(OlcumKind.Click, null, action);
        if (decision)
            Current?.Write(OlcumKind.Decision, null, action);
    }

    public static void Decided(string source) => Click(source, true);

    public static void Undone(string source) => Current?.Write(OlcumKind.Undo, null, source);

    public void Write(string kind, long? value = null, string? detail = null, bool once = false)
    {
        lock (_lock)
        {
            if (once && !_seen.Add(kind))
                return;
            var line = JsonSerializer.Serialize(new OlcumEvent(_clock(), Session, kind, value, detail), OlcumJson.Default.OlcumEvent);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, line + "\n", Encoding.UTF8);
                if (new FileInfo(Path).Length > MaxBytes)
                    TrimOldHalf();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    void TrimOldHalf()
    {
        var bytes = File.ReadAllBytes(Path);
        var from = bytes.Length - (int)(MaxBytes / 2);
        while (from < bytes.Length && bytes[from - 1] != (byte)'\n')
            from++;
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, bytes.AsSpan(from).ToArray());
        File.Move(temp, Path, overwrite: true);
    }

    public static OlcumSummary? Summarize(IEnumerable<string> lines)
    {
        var events = new List<OlcumEvent>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonSerializer.Deserialize(line, OlcumJson.Default.OlcumEvent) is { Session: not null, Kind: not null } e)
                    events.Add(e);
            }
            catch (JsonException)
            {
            }
        }
        if (events.Count == 0)
            return null;
        var session = events.LastOrDefault(e => e.Kind == OlcumKind.Start)?.Session ?? events[^1].Session;
        var mine = events.Where(e => e.Session == session).ToList();
        var started = mine.FirstOrDefault(e => e.Kind == OlcumKind.Start)?.At ?? mine[0].At;
        var card = mine.FirstOrDefault(e => e.Kind == OlcumKind.FirstCard);
        var freed = mine.FirstOrDefault(e => e.Kind == OlcumKind.FirstFreed);
        return new OlcumSummary(
            session,
            started,
            card is null ? null : card.At - started,
            freed is null ? null : freed.At - started,
            freed?.Value ?? 0,
            mine.Count(e => e.Kind == OlcumKind.Decision),
            mine.Count(e => e.Kind == OlcumKind.Click),
            mine.Count(e => e.Kind == OlcumKind.Undo));
    }

    public static OlcumSummary? Summarize(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file) ? Summarize(File.ReadLines(file, Encoding.UTF8)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool Reveal(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            var arguments = File.Exists(file)
                ? $"/select,\"{file}\""
                : $"\"{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!).FullName}\"";
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
            return process is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
