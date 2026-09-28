using System.Text.Json.Serialization;

namespace DustyBytes.App.Services;

public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized, double Zoom = 0);

public sealed record LedgerData(long FreedBytes, int Actions);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(LedgerData))]
public partial class AppJson : JsonSerializerContext;
