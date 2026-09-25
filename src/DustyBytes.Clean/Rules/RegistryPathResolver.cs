using Microsoft.Win32;

namespace DustyBytes.Clean.Rules;

public static class RegistryPathResolver
{
    public static (RegistryKey Root, string SubKey)? Split(string path)
    {
        var parts = path.Split('\\', 2);
        if (parts.Length != 2)
            return null;
        RegistryKey? root = parts[0].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => null,
        };
        return root is null ? null : (root, parts[1]);
    }
}
