using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustyBytes.Core.Protection;

public sealed class ProtectedRules
{
    public List<PathRule> Roots { get; set; } = [];
    public List<NameRule> Segments { get; set; } = [];
    public List<NameRule> DriveRoots { get; set; } = [];
    public List<NameRule> Files { get; set; } = [];
    public List<PrefixRule> RuntimePrefixes { get; set; } = [];
    public List<CloudRule> Cloud { get; set; } = [];
    public List<string> NeverLeftover { get; set; } = [];
    public List<string> Appx { get; set; } = [];

    public static ProtectedRules Load(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize(stream, ProtectedRulesJson.Default.ProtectedRules)
            ?? throw new InvalidDataException($"Korumalı liste okunamadı: {file}");
    }

    public static ProtectedRules LoadDefault() =>
        Load(Path.Combine(Paths.RulesDir, "protected.json"));
}

public sealed class PathRule
{
    public string Path { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class NameRule
{
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class PrefixRule
{
    public string Prefix { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CloudRule
{
    public string? Env { get; set; }
    public string? Path { get; set; }
    public string Reason { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, WriteIndented = true)]
[JsonSerializable(typeof(ProtectedRules))]
[JsonSerializable(typeof(List<string>))]
public partial class ProtectedRulesJson : JsonSerializerContext;
