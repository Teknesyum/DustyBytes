using System.Text.Json;
using System.Text.Json.Serialization;
using DustyBytes.Core;

namespace DustyBytes.Units;

public sealed class KnownContent
{
    public string Owner { get; set; } = "";
    public string What { get; set; } = "";
    public List<string> Patterns { get; set; } = [];
    public string Effect { get; set; } = "";
    public bool Each { get; set; }
    public long MinBytes { get; set; } = 1L << 30;
    public bool UserData { get; set; }
}

public sealed class KnownContentRules
{
    public List<KnownContent> Items { get; set; } = [];

    public static KnownContentRules Load(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize(stream, KnownContentRulesJson.Default.KnownContentRules)
            ?? throw new InvalidDataException($"Bilinen içerik kuralları okunamadı: {file}");
    }

    public static KnownContentRules LoadDefault() =>
        Load(Path.Combine(Paths.RulesDir, "known-content.json"));
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(KnownContentRules))]
public partial class KnownContentRulesJson : JsonSerializerContext;
