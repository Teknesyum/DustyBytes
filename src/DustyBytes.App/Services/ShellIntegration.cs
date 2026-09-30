using Microsoft.Win32;

namespace DustyBytes.App.Services;

public interface IUserRegistry
{
    string? Read(string key, string? name);
    void Write(string key, string? name, string value);
    void DeleteTree(string key);
    bool Exists(string key);
}

public sealed class CurrentUserRegistry : IUserRegistry
{
    public string? Read(string key, string? name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name ?? "") as string;
    }

    public void Write(string key, string? name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k.SetValue(name ?? "", value, RegistryValueKind.String);
    }

    public void DeleteTree(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);

    public bool Exists(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k is not null;
    }
}

public sealed record RegistryValue(string Key, string? Name, string Value);

public sealed class ShellIntegration(IUserRegistry registry, string exePath)
{
    public const string AppId = "Teknesyum.DustyBytes";
    public const string MenuKey = @"Software\Classes\Directory\shell\DustyBytes";
    public const string ProtocolKey = @"Software\Classes\" + LaunchArgs.Scheme;
    public const string AppIdKey = @"Software\Classes\AppUserModelId\" + AppId;
    public const string MenuText = "DustyBytes ile incele";

    public static readonly string[] Keys = [MenuKey, ProtocolKey, AppIdKey];

    public string ExePath { get; } = exePath;

    public IReadOnlyList<RegistryValue> Wanted()
    {
        var exe = $"\"{ExePath}\"";
        var icon = ExePath + ",0";
        List<RegistryValue> values =
        [
            new(MenuKey, null, MenuText),
            new(MenuKey, "Icon", icon),
            new(MenuKey + @"\command", null, $"{exe} {LaunchArgs.Inspect} \"%1\""),
            new(ProtocolKey, null, "URL:DustyBytes"),
            new(ProtocolKey, "URL Protocol", ""),
            new(ProtocolKey + @"\DefaultIcon", null, icon),
            new(ProtocolKey + @"\shell\open\command", null, $"{exe} \"%1\""),
            new(AppIdKey, "DisplayName", "DustyBytes"),
        ];
        var png = Path.Combine(Path.GetDirectoryName(ExePath) ?? "", "DustyBytes.png");
        if (File.Exists(png))
            values.Add(new(AppIdKey, "IconUri", png));
        return values;
    }

    public int Ensure()
    {
        var written = 0;
        foreach (var v in Wanted())
        {
            if (registry.Read(v.Key, v.Name) == v.Value)
                continue;
            registry.Write(v.Key, v.Name, v.Value);
            written++;
        }
        return written;
    }

    public int Remove()
    {
        var removed = 0;
        foreach (var key in Keys)
        {
            if (!registry.Exists(key))
                continue;
            registry.DeleteTree(key);
            removed++;
        }
        return removed;
    }
}
