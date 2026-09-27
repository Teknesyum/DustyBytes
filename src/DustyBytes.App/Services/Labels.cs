using System.Text.Json;
using Avalonia.Platform;

namespace DustyBytes.App.Services;

public static class Labels
{
    static readonly Dictionary<string, string> Values = Load();

    public static string Brand => Get("sig.brand");
    public static string BrandTitle => Get("sig.brandTitle");
    public static string Support => Get("sig.support");
    public static string SupportTitle => Get("sig.supportTitle");
    public static string Update => Get("update.label");
    public static string UpdateDownload => Get("update.download");
    public static string UpdateInstall => Get("update.install");

    public static string Get(string key) => Values.TryGetValue(key, out var v) ? v : key;

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>();
        using var s = AssetLoader.Open(new Uri("avares://DustyBytes/Tema/labels.tr.json"));
        using var doc = JsonDocument.Parse(s);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString()!;
        return d;
    }
}
