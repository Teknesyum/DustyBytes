using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Uninstall;

public static partial class RegistryGate
{
    const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion\";

    static readonly string[] KeyAllowUnderMicrosoft =
    [
        CurrentVersion + "Uninstall",
        CurrentVersion + "App Paths",
        CurrentVersion + @"Explorer\ShellIconOverlayIdentifiers",
    ];

    static readonly string[] ValueAllowKeys =
    [
        CurrentVersion + "Run",
        CurrentVersion + "RunOnce",
        CurrentVersion + @"Explorer\StartupApproved\Run",
        CurrentVersion + @"Explorer\StartupApproved\Run32",
        CurrentVersion + @"Explorer\StartupApproved\StartupFolder",
        CurrentVersion + @"Shell Extensions\Approved",
        LeftoverScanner.FirewallRulesPath,
    ];

    static readonly HashSet<string> ClassContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLSID", "TypeLib", "Interface", "AppID", "*", "Directory", "Folder", "Drive", "AllFilesystemObjects",
        "DesktopBackground", "Applications", "Installer", "SystemFileAssociations", "Local Settings", "WOW6432Node",
        "exefile", "lnkfile", "txtfile", "batfile", "cmdfile", "comfile", "inifile", "regfile", "htmlfile", "http", "https",
        "mailto", "Unknown", "ms-settings", "Network", "Printers", "PropertySystem", "MIME", "Protocols", "Wow6432Node",
    };

    static readonly HashSet<string> ComContainers = new(StringComparer.OrdinalIgnoreCase) { "CLSID", "TypeLib", "Interface", "AppID" };

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex Guid();

    public static bool IsGuid(string s) => Guid().IsMatch(s);

    public static string Logical(RegKeyRef key)
    {
        var path = key.Path.Trim('\\');
        if (key.Hive == RegHive.Users)
        {
            var i = path.IndexOf('\\');
            var first = i < 0 ? path : path[..i];
            var rest = i < 0 ? "" : path[(i + 1)..];
            if (first.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                return rest.Length == 0 ? @"SOFTWARE\Classes" : @"SOFTWARE\Classes\" + rest;
            path = rest;
        }
        if (path.StartsWith(@"SOFTWARE\WOW6432Node\", StringComparison.OrdinalIgnoreCase))
            path = @"SOFTWARE\" + path[21..];
        else if (path.Equals(@"SOFTWARE\WOW6432Node", StringComparison.OrdinalIgnoreCase))
            path = "SOFTWARE";
        if (path.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase) || path.Equals("Software", StringComparison.OrdinalIgnoreCase))
            path = "SOFTWARE" + path[8..];
        return path;
    }

    static bool Under(string path, string root) =>
        path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    static bool Is(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase);

    public static string? KeyBlock(RegKeyRef key)
    {
        var path = Logical(key);
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "Kök düzeyinde anahtar";
        if (parts[0].Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 4 && Under(path, LeftoverScanner.ServicesPath) && !parts[3].Equals("EventLog", StringComparison.OrdinalIgnoreCase)
                ? null
                : "Sistem anahtarı";
        if (!parts[0].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
            return "Yazılım anahtarı dışında";
        var second = parts[1];
        if (second.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var allow in KeyAllowUnderMicrosoft)
                if (Under(path, allow) && parts.Length == allow.Split('\\').Length + 1)
                    return null;
            return "Microsoft anahtarı; yalnız programın kendi Uninstall, App Paths ve simge kaplaması girdisine dokunulur";
        }
        if (second.Equals("Windows", StringComparison.OrdinalIgnoreCase) || second.Equals("Policies", StringComparison.OrdinalIgnoreCase))
            return $"{second} anahtarı asla silinmez";
        if (second.Equals("Classes", StringComparison.OrdinalIgnoreCase))
            return ClassesBlock(parts);
        if (second.Equals("RegisteredApplications", StringComparison.OrdinalIgnoreCase) || second.Equals("Clients", StringComparison.OrdinalIgnoreCase) && parts.Length < 4
            || second.Equals("ODBC", StringComparison.OrdinalIgnoreCase) || second.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            return "Paylaşılan kayıt alanı";
        return null;
    }

    static string? ClassesBlock(string[] parts)
    {
        if (parts.Length < 3)
            return "Classes kökü";
        var name = parts[2];
        if (name.StartsWith('.'))
            return "Dosya uzantısı anahtarı (Classes\\.ext) asla silinmez; yalnız programı gösteren değer temizlenir";
        var shellex = Array.FindIndex(parts, p => p.Equals("shellex", StringComparison.OrdinalIgnoreCase));
        if (shellex > 2)
            return parts.Length >= shellex + 3 ? null : "Kabuk uzantısı kabı";
        if (ComContainers.Contains(name))
            return parts.Length == 4 && IsGuid(parts[3]) ? null : "COM kabı; yalnız kimliği doğrulanmış tek girdi silinir";
        if (name.Equals("Applications", StringComparison.OrdinalIgnoreCase))
            return parts.Length >= 4 ? null : "Applications kabı";
        if (ClassContainers.Contains(name) || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("AppX", StringComparison.OrdinalIgnoreCase))
            return "Windows'un sınıf anahtarı";
        return null;
    }

    public static string? ValueBlock(RegKeyRef key, string valueName)
    {
        var path = Logical(key);
        foreach (var allow in ValueAllowKeys)
            if (Is(path, allow))
                return null;
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[0].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("Classes", StringComparison.OrdinalIgnoreCase) && parts[2].StartsWith('.'))
            return parts.Length == 3 || parts.Length == 4 && parts[3].Equals("OpenWithProgids", StringComparison.OrdinalIgnoreCase)
                ? null
                : "Uzantı altındaki bu alan korunur";
        if (Under(path, CurrentVersion + @"Explorer\FileExts") && parts.Length == 8 && parts[7].Equals("OpenWithProgids", StringComparison.OrdinalIgnoreCase))
            return null;
        if (path.Contains("SharedDLLs", StringComparison.OrdinalIgnoreCase))
            return "SharedDLLs sayacı; yalnız sayaç düşürülür";
        return KeyBlock(key);
    }

    public static string? Check(LeftoverCandidate c) => c.Key is not { } key ? null : c.Kind switch
    {
        LeftoverKind.RegistryValue or LeftoverKind.StartupEntry or LeftoverKind.FirewallRule when c.ValueName is { } v => ValueBlock(key, v),
        LeftoverKind.RegistryKey or LeftoverKind.FileAssociation or LeftoverKind.Service => KeyBlock(key),
        _ => null,
    };
}
