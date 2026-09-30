using System.Text.Json.Serialization;

namespace DustyBytes.App.Services;

public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized, double Zoom = 0);

public sealed record FreedEntry(DateTimeOffset At, long Bytes, string? Root = null, long TotalBytes = 0, long FreeAfter = 0)
{
    [JsonIgnore]
    public bool HasDisk => TotalBytes > 0 && FreeAfter >= 0 && FreeAfter <= TotalBytes;

    [JsonIgnore]
    public double UsedBefore => HasDisk ? Math.Clamp(1 - (double)Math.Max(0, FreeAfter - Bytes) / TotalBytes, 0, 1) : 0;

    [JsonIgnore]
    public double UsedAfter => HasDisk ? Math.Clamp(1 - (double)FreeAfter / TotalBytes, 0, 1) : 0;
}

public sealed record LedgerData(long FreedBytes, int Actions, IReadOnlyList<FreedEntry>? History = null)
{
    [JsonIgnore]
    public IReadOnlyList<FreedEntry> Entries => History ?? [];

    [JsonIgnore]
    public FreedEntry? Last => Entries.Count > 0 ? Entries[^1] : null;

    public long FreedInMonth(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        return Entries.Where(e => e.At.ToLocalTime() is var at && at.Year == local.Year && at.Month == local.Month).Sum(e => e.Bytes);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(LedgerData))]
public partial class AppJson : JsonSerializerContext;
