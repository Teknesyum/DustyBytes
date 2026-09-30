using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustyBytes.Core;

public sealed record AppSettings
{
    public static readonly TimeSpan QuarantineDays = TimeSpan.FromDays(7);

    public bool AutoPurge { get; init; } = true;

    public bool WeeklyCheck { get; init; } = true;
    public bool ScanRemovable { get; init; }
    public long DuplicateMinBytes { get; init; } = 10L * 1024 * 1024;

    public static string DefaultPath => Path.Combine(Paths.AppData, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file))
                return new AppSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(file), SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        File.Move(tmp, file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class SettingsJson : JsonSerializerContext;
