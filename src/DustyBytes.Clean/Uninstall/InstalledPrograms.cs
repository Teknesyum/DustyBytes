using System.Globalization;

namespace DustyBytes.Clean.Uninstall;

public sealed record EnumerateOptions
{
    public bool IncludeMsix { get; init; } = true;
    public bool MeasureSize { get; init; } = true;
    public bool DetectBySignature { get; init; } = true;
    public bool IncludeHidden { get; init; }
    public string? UserSid { get; init; }
}

public static class InstalledPrograms
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string MsiUserDataProducts = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";

    static readonly (RegHive Hive, RegView View)[] Views =
    [
        (RegHive.LocalMachine, RegView.Registry64),
        (RegHive.LocalMachine, RegView.Registry32),
        (RegHive.CurrentUser, RegView.Registry64),
        (RegHive.CurrentUser, RegView.Registry32),
    ];

    static readonly string[] HiddenReleaseTypes = ["Update", "Hotfix", "Security Update", "Service Pack", "Update Rollup"];

    public static IReadOnlyList<InstalledProgram> Enumerate(EnumerateOptions? options = null) =>
        Enumerate(WindowsRegistryView.Instance, FileProbe.Instance, options);

    public static IReadOnlyList<InstalledProgram> Enumerate(IRegistryView reg, IFileProbe? probe, EnumerateOptions? options = null)
    {
        options ??= new EnumerateOptions();
        var list = EnumerateRegistry(reg, probe, options);
        if (options.IncludeMsix)
            list.AddRange(MsixPackages.Enumerate());
        if (options.MeasureSize)
            MeasureSizes(list);
        return list;
    }

    public static List<InstalledProgram> EnumerateRegistry(IRegistryView reg, IFileProbe? probe, EnumerateOptions options)
    {
        var result = new List<InstalledProgram>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentity = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in Views)
        {
            var root = hive == RegHive.CurrentUser && options.UserSid is { } sid
                ? new RegKeyRef(RegHive.Users, view, sid + "\\" + UninstallPath)
                : new RegKeyRef(hive, view, UninstallPath);
            foreach (var keyName in reg.GetSubKeyNames(root))
            {
                var key = root.Child(keyName);
                var program = Read(reg, probe, key, keyName, options);
                if (program is null)
                    continue;

                var keyIdentity = $"{hive}|{keyName}|{program.DisplayName}";
                if (!seenKeys.Add(keyIdentity))
                    continue;
                if (program.ProductCode is { } pc && !seenProducts.Add(pc))
                    continue;
                var identity = $"{program.DisplayName}|{program.DisplayVersion}|{program.Publisher}|{program.InstallLocation}";
                if (!seenIdentity.Add(identity))
                    continue;

                result.Add(program);
            }
        }
        return result;
    }

    static InstalledProgram? Read(IRegistryView reg, IFileProbe? probe, RegKeyRef key, string keyName, EnumerateOptions options)
    {
        var name = reg.GetString(key, "DisplayName");
        if (name is null)
            return null;

        if (!options.IncludeHidden)
        {
            if (reg.GetNumber(key, "SystemComponent") == 1)
                return null;
            if (reg.GetString(key, "ParentKeyName") is not null)
                return null;
            if (reg.GetString(key, "ReleaseType") is { } rt && HiddenReleaseTypes.Contains(rt, StringComparer.OrdinalIgnoreCase))
                return null;
        }

        var windowsInstaller = reg.GetNumber(key, "WindowsInstaller") == 1;
        var uninstall = reg.GetString(key, "UninstallString");
        var quiet = reg.GetString(key, "QuietUninstallString");
        string? productCode = null;
        if (MsiGuid.TryParseBraced(keyName, out var g))
            productCode = MsiGuid.Format(g);
        else if (windowsInstaller && reg.GetString(key, "ProductCode") is { } pcv && MsiGuid.TryParseBraced(pcv, out var g2))
            productCode = MsiGuid.Format(g2);
        if (!windowsInstaller && uninstall?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) != true)
            productCode = null;

        var location = NormalizeLocation(reg.GetString(key, "InstallLocation"));
        if (location is null && windowsInstaller && productCode is not null)
            location = NormalizeLocation(MsiInstallLocation(reg, productCode));

        var estimatedKb = reg.GetNumber(key, "EstimatedSize") ?? 0;
        var installer = InstallerDetector.Detect(keyName, windowsInstaller, uninstall, quiet, reg, key, options.DetectBySignature ? probe : null);
        var perUser = key.Hive != RegHive.LocalMachine;
        var id = $"reg:{(perUser ? "HKCU" : "HKLM")}{(key.View == RegView.Registry32 ? "32" : "64")}:{keyName}";

        var program = new InstalledProgram
        {
            Id = id,
            DisplayName = name,
            Source = ProgramSource.Registry,
            Publisher = reg.GetString(key, "Publisher"),
            DisplayVersion = reg.GetString(key, "DisplayVersion"),
            InstallLocation = location,
            InstallDate = ParseDate(reg.GetString(key, "InstallDate")),
            EstimatedSizeBytes = Math.Max(0, estimatedKb) * 1024,
            SizeBytes = Math.Max(0, estimatedKb) * 1024,
            UninstallString = uninstall,
            QuietUninstallString = quiet,
            WindowsInstaller = windowsInstaller,
            ProductCode = productCode,
            DisplayIcon = reg.GetString(key, "DisplayIcon"),
            Installer = installer,
            Key = key,
            KeyName = keyName,
            Is64Bit = key.View == RegView.Registry64 && !perUser,
            PerUser = perUser,
            NoRemove = reg.GetNumber(key, "NoRemove") == 1,
        };
        return probe is null ? program : program with { UninstallerMissing = ForceUninstall.BrokenReason(program, reg, probe) is not null };
    }

    public static string? MsiInstallLocation(IRegistryView reg, string productCode)
    {
        if (!MsiGuid.TryParseBraced(productCode, out var g))
            return null;
        var props = new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, MsiUserDataProducts).Child(MsiGuid.Compress(g)).Child("InstallProperties");
        return reg.GetString(props, "InstallLocation");
    }

    public static bool MsiProductRegistered(IRegistryView reg, string productCode)
    {
        if (!MsiGuid.TryParseBraced(productCode, out var g))
            return false;
        var packed = MsiGuid.Compress(g);
        return reg.KeyExists(new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, MsiUserDataProducts).Child(packed))
            || reg.KeyExists(new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes\Installer\Products").Child(packed));
    }

    static string? NormalizeLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"').Trim());
        if (s.Length < 3 || s[1] != ':')
            return null;
        try
        {
            return DustyBytes.Core.Paths.Normalize(s);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    static DateOnly? ParseDate(string? raw)
    {
        if (raw is null)
            return null;
        if (DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d;
        return null;
    }

    public static void MeasureSizes(List<InstalledProgram> list)
    {
        var measured = new InstalledProgram[list.Count];
        Parallel.For(0, list.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, i =>
        {
            var p = list[i];
            measured[i] = p;
            if (p.InstallLocation is not { } loc || BroadPaths.IsTooBroad(loc) || !Directory.Exists(loc))
                return;
            var size = FolderSize.Measure(loc);
            if (size >= 0)
                measured[i] = p with { SizeBytes = size, SizeMeasured = true };
        });
        for (var i = 0; i < list.Count; i++)
            list[i] = measured[i];
    }
}
