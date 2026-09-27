namespace DustyBytes.Clean.Uninstall;

public sealed record UserScope(string? Sid, string? ProfilePath, string? Note = null)
{
    public const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    const string ShellFoldersPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

    public static UserScope Fallback(string reason) =>
        new(null, null, $"{reason}; geçerli kullanıcının kaydı (HKCU) ve klasörleri tarandı");

    public static UserScope Resolve(string? sid, IRegistryView registry)
    {
        if (string.IsNullOrWhiteSpace(sid) || !sid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            return Fallback("Arayüzün kullanıcısı belirlenemedi");
        if (!registry.KeyExists(new RegKeyRef(RegHive.Users, RegView.Registry64, sid)))
            return Fallback($"Kullanıcı kaydı yüklü değil ({sid})");
        var profile = registry.GetString(new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, ProfileListPath).Child(sid), "ProfileImagePath");
        return new UserScope(sid, profile is null ? null : Environment.ExpandEnvironmentVariables(profile));
    }

    public string? Folder(IRegistryView registry, string name)
    {
        if (Sid is null)
            return null;
        foreach (var list in new[] { "User Shell Folders", "Shell Folders" })
        {
            var raw = registry.GetString(new RegKeyRef(RegHive.Users, RegView.Registry64, $@"{Sid}\{ShellFoldersPath}\{list}"), name);
            if (raw is null)
                continue;
            if (raw.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            {
                if (ProfilePath is null)
                    continue;
                raw = raw.Replace("%USERPROFILE%", ProfilePath, StringComparison.OrdinalIgnoreCase);
            }
            if (!raw.Contains('%') && Path.IsPathRooted(raw))
                return raw;
        }
        return null;
    }
}
