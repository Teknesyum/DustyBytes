using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DustyBytes.Clean.Uninstall;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryView : IRegistryView
{
    public static readonly WindowsRegistryView Instance = new();

    static RegistryKey Base(RegKeyRef key)
    {
        var hive = key.Hive switch
        {
            RegHive.LocalMachine => RegistryHive.LocalMachine,
            RegHive.CurrentUser => RegistryHive.CurrentUser,
            _ => RegistryHive.Users,
        };
        return RegistryKey.OpenBaseKey(hive, key.View == RegView.Registry32 ? RegistryView.Registry32 : RegistryView.Registry64);
    }

    static RegistryKey? Open(RegKeyRef key, bool writable = false)
    {
        try
        {
            using var root = Base(key);
            return key.Path.Length == 0 ? Base(key) : root.OpenSubKey(key.Path, writable);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool KeyExists(RegKeyRef key)
    {
        using var k = Open(key);
        return k is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegKeyRef key)
    {
        using var k = Open(key);
        if (k is null)
            return [];
        try
        {
            return k.GetSubKeyNames();
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> GetValueNames(RegKeyRef key)
    {
        using var k = Open(key);
        if (k is null)
            return [];
        try
        {
            return k.GetValueNames();
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public RegValue? GetValue(RegKeyRef key, string name)
    {
        using var k = Open(key);
        if (k is null)
            return null;
        try
        {
            var data = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (data is null)
                return null;
            var kind = k.GetValueKind(name) switch
            {
                RegistryValueKind.String => RegKind.String,
                RegistryValueKind.ExpandString => RegKind.ExpandString,
                RegistryValueKind.MultiString => RegKind.MultiString,
                RegistryValueKind.DWord => RegKind.DWord,
                RegistryValueKind.QWord => RegKind.QWord,
                RegistryValueKind.Binary => RegKind.Binary,
                RegistryValueKind.None => RegKind.None,
                _ => RegKind.Unknown,
            };
            return new RegValue(name, kind, data);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool DeleteKeyTree(RegKeyRef key)
    {
        var parent = key.Parent();
        if (parent is null)
            return false;
        using var p = Open(parent, writable: true);
        if (p is null)
            return false;
        try
        {
            p.DeleteSubKeyTree(key.Name, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool DeleteValue(RegKeyRef key, string name)
    {
        using var k = Open(key, writable: true);
        if (k is null)
            return false;
        try
        {
            k.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
