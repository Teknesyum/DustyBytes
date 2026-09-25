using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustyBytes.Clean.Rules;

public enum CleanActionType
{
    Delete,
    RegistryDelete,
}

public enum DeleteMode
{
    Glob,
    Recurse,
    FilesOnly,
}

public sealed class CleanAction
{
    public CleanActionType Type { get; set; }
    public DeleteMode? Mode { get; set; }
    public string? Path { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
}

public sealed class RunningTest
{
    public List<string> ProcessNames { get; set; } = [];
    public List<string> LockFiles { get; set; } = [];

    public bool HasEvidence => ProcessNames.Count > 0 || LockFiles.Count > 0;
}

public sealed class CleanerOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Warning { get; set; }
    public List<CleanAction> Actions { get; set; } = [];
}

public sealed class CleanerRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Source { get; set; }
    public RunningTest Running { get; set; } = new();
    public List<CleanerOption> Options { get; set; } = [];

    public static CleanerRule Load(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize(stream, CleanerRuleJson.Default.CleanerRule)
            ?? throw new InvalidDataException($"Temizlik kuralı okunamadı: {file}");
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    Converters = [typeof(JsonStringEnumConverter<CleanActionType>), typeof(JsonStringEnumConverter<DeleteMode>)])]
[JsonSerializable(typeof(CleanerRule))]
public partial class CleanerRuleJson : JsonSerializerContext;
