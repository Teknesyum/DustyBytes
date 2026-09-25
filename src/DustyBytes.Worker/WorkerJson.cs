using System.Text.Json.Serialization;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;

namespace DustyBytes.Worker;

public sealed record QuarantineListing(List<QuarantineEntry> Entries, List<VolumeUsage> Usage);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuarantineListing))]
[JsonSerializable(typeof(List<LockHolder>))]
[JsonSerializable(typeof(List<OpResult>))]
[JsonSerializable(typeof(List<RecycleBinItem>))]
[JsonSerializable(typeof(string[]))]
public partial class WorkerJson : JsonSerializerContext;
