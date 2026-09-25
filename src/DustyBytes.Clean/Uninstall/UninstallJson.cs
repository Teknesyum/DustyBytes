using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = false)]
[JsonSerializable(typeof(LeftoverSnapshot))]
[JsonSerializable(typeof(InstalledProgram))]
[JsonSerializable(typeof(List<InstalledProgram>))]
[JsonSerializable(typeof(RemovalReport))]
public partial class UninstallJson : JsonSerializerContext;

public sealed class SnapshotStore
{
    readonly string _dir;

    public SnapshotStore(string? dir = null) => _dir = dir ?? Path.Combine(Paths.AppData, "uninstall");

    public string Save(LeftoverSnapshot snapshot)
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, snapshot.Id + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, UninstallJson.Default.LeftoverSnapshot));
        return file;
    }

    public LeftoverSnapshot? Load(string? id)
    {
        if (id is null || id.Length != 32 || !id.All(Uri.IsHexDigit))
            return null;
        var file = Path.Combine(_dir, id + ".json");
        if (!File.Exists(file))
            return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), UninstallJson.Default.LeftoverSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
