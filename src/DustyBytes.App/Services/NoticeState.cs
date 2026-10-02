using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;
using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public sealed record NoticeData(
    long SafeBytes = 0,
    long ReclaimableBytes = 0,
    DateTimeOffset? EstimateAt = null,
    DateTimeOffset? LastShown = null,
    DateTimeOffset? SnoozedUntil = null,
    string? Token = null,
    DateTimeOffset? TokenAt = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NoticeData))]
public partial class NoticeJson : JsonSerializerContext;

public sealed class NoticeState(string? path = null)
{
    public static readonly TimeSpan TokenLife = TimeSpan.FromDays(14);

    readonly Lock _lock = new();

    public static NoticeState? Current { get; set; }

    public static string DefaultPath => System.IO.Path.Combine(Paths.AppData, "notice.json");

    public string Path { get; } = path ?? DefaultPath;

    public NoticeData Read()
    {
        lock (_lock)
            return Load();
    }

    NoticeData Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), NoticeJson.Default.NoticeData) ?? new NoticeData()
                : new NoticeData();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new NoticeData();
        }
    }

    NoticeData Update(Func<NoticeData, NoticeData> change)
    {
        lock (_lock)
        {
            var next = change(Load());
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var temp = Path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(next, NoticeJson.Default.NoticeData));
                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            return next;
        }
    }

    public void RecordEstimate(long safeBytes, long reclaimableBytes, DateTimeOffset? at = null) =>
        Update(d => d with { SafeBytes = Math.Max(0, safeBytes), ReclaimableBytes = Math.Max(0, reclaimableBytes), EstimateAt = at ?? DateTimeOffset.Now });

    public void MarkShown(DateTimeOffset at) => Update(d => d with { LastShown = at });

    public string IssueToken(DateTimeOffset at)
    {
        var token = Guid.NewGuid().ToString("N");
        Update(d => d with { Token = token, TokenAt = at });
        return token;
    }

    public bool Redeem(string? token, DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.Now;
        var ok = false;
        Update(d =>
        {
            ok = token is { Length: > 0 } && d.Token == token && d.TokenAt is { } issued && now - issued <= TokenLife && now >= issued;
            return ok ? d with { Token = null, TokenAt = null } : d;
        });
        return ok;
    }

    public bool Snooze(string? token, DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.Now;
        if (!Redeem(token, now))
            return false;
        Update(d => d with { SnoozedUntil = now + NoticePolicy.SnoozeSpan });
        return true;
    }

    public static NoticeState ForCurrentUser() => new();
}

public static class NoticePolicy
{
    public const long MinReclaimable = 5L << 30;
    public static readonly TimeSpan MinGap = TimeSpan.FromDays(6);
    public static readonly TimeSpan SnoozeSpan = TimeSpan.FromDays(7);

    public static bool Allows(IReadOnlyList<DriveSpace> low, NoticeData data, DateTimeOffset now)
    {
        if (low.Count == 0)
            return false;
        if (data.SnoozedUntil is { } until && now < until)
            return false;
        if (data.LastShown is { } last && now - last < MinGap)
            return false;
        return low.Any(DiskCheck.IsCritical) || data.ReclaimableBytes >= MinReclaimable;
    }

    public static string? SafeButtonText(NoticeData data)
    {
        if (data.EstimateAt is null)
            return "Güvenli temizle";
        return data.SafeBytes > 0 ? $"Güvenli temizle (≈{Format.Bytes(data.SafeBytes)})" : null;
    }

    public static IReadOnlyList<ToastAction> Actions(NoticeData data, string token)
    {
        var actions = new List<ToastAction>();
        if (SafeButtonText(data) is { } safe)
            actions.Add(new ToastAction(safe, LaunchArgs.ActionUri(LaunchArgs.SafeCleanAction, token)));
        actions.Add(new ToastAction("Bu hafta sus", LaunchArgs.ActionUri(LaunchArgs.SnoozeAction, token)));
        actions.Add(new ToastAction("Bir daha gösterme", LaunchArgs.ActionUri(LaunchArgs.MuteAction, token)));
        return actions;
    }
}

public static class NoticeActions
{
    public static bool Mute(NoticeState state, string? token, Func<bool> disableWeekly, DateTimeOffset? at = null)
    {
        if (!state.Redeem(token, at))
            return false;
        return disableWeekly();
    }

    public static bool DisableWeekly()
    {
        try
        {
            (AppSettings.Load() with { WeeklyCheck = false }).Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        try
        {
            SystemIntegration.ForCurrentUser().SetWeekly(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
        return true;
    }
}
