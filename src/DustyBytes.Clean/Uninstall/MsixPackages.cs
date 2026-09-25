using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace DustyBytes.Clean.Uninstall;

public sealed record MsixRemoveResult(bool Ok, string Message);

public static class MsixPackages
{
    public static IReadOnlyList<InstalledProgram> Enumerate(bool includeSystem = false)
    {
        var list = new List<InstalledProgram>();
        IEnumerable<Package> packages;
        try
        {
            packages = new PackageManager().FindPackagesForUser("");
        }
        catch (Exception)
        {
            return list;
        }

        foreach (var p in packages)
        {
            try
            {
                if (p.IsFramework || p.IsResourcePackage)
                    continue;
                if (!includeSystem && p.SignatureKind == PackageSignatureKind.System)
                    continue;
                var id = p.Id;
                var name = Safe(() => p.DisplayName);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    name = id.Name;
                var v = id.Version;
                list.Add(new InstalledProgram
                {
                    Id = "msix:" + id.FullName,
                    DisplayName = name!,
                    Source = ProgramSource.Msix,
                    Publisher = Safe(() => p.PublisherDisplayName) ?? id.Publisher,
                    DisplayVersion = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    InstallLocation = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? Safe(() => p.InstalledPath) : Safe(() => p.InstalledLocation?.Path),
                    InstallDate = Safe(() => (DateOnly?)DateOnly.FromDateTime(p.InstalledDate.LocalDateTime)),
                    Installer = InstallerType.Msix,
                    PackageFullName = id.FullName,
                    PackageFamilyName = id.FamilyName,
                    Is64Bit = (int)id.Architecture is 9 or 12,
                    PerUser = true,
                    IsFramework = false,
                });
            }
            catch (Exception)
            {
            }
        }
        return list;
    }

    public static bool Exists(string packageFullName)
    {
        try
        {
            return new PackageManager().FindPackageForUser("", packageFullName) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<MsixRemoveResult> RemoveAsync(string packageFullName, IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageFullName) || packageFullName.IndexOfAny(['*', '?']) >= 0)
            return new(false, "Paket tam adı geçersiz; joker karakter kabul edilmez");
        if (!Exists(packageFullName))
            return new(false, $"Paket bulunamadı: {packageFullName}");
        try
        {
            var op = new PackageManager().RemovePackageAsync(packageFullName);
            if (progress is not null)
                op.Progress = (_, p) => progress.Report(p.percentage);
            var result = await op.AsTask(ct).ConfigureAwait(false);
            return result.ExtendedErrorCode is null || result.ExtendedErrorCode.HResult == 0
                ? new(true, "Paket kaldırıldı")
                : new(false, $"Paket kaldırılamadı: {result.ErrorText} (0x{result.ExtendedErrorCode.HResult:X8})");
        }
        catch (OperationCanceledException)
        {
            return new(false, "İptal edildi");
        }
        catch (Exception e)
        {
            return new(false, $"Paket kaldırılamadı: {e.Message}");
        }
    }

    static T? Safe<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
