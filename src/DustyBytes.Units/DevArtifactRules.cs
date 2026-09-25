using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;

namespace DustyBytes.Units;

public sealed class DevEcosystem
{
    public string Name { get; set; } = "";
    public List<string> Markers { get; set; } = [];
    public List<string> ArtifactDirs { get; set; } = [];
}

public sealed class DevArtifactRules
{
    public List<DevEcosystem> Ecosystems { get; set; } = [];

    public static DevArtifactRules Load(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize(stream, DevArtifactRulesJson.Default.DevArtifactRules)
            ?? throw new InvalidDataException($"Geliştirici artığı kuralları okunamadı: {file}");
    }

    public static DevArtifactRules LoadDefault() =>
        Load(Path.Combine(Paths.RulesDir, "dev-artifacts.json"));
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(DevArtifactRules))]
public partial class DevArtifactRulesJson : JsonSerializerContext;
