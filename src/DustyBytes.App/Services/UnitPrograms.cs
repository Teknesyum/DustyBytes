using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Protection;
using DustyBytes.Units;

namespace DustyBytes.App.Services;

public static class UnitPrograms
{
    public static IReadOnlyList<ProgramInstall> From(IEnumerable<InstalledProgram> programs, ProtectedList protection) =>
        [.. programs
            .Where(p => !p.IsFramework && p.CanUninstall && p.Source != ProgramSource.Msix)
            .Where(p => p.InstallLocation is { Length: > 0 })
            .Where(p => protection.RuntimeReason(p.DisplayName) is null)
            .Select(p => new ProgramInstall(p.Id, p.DisplayName, p.InstallLocation!, p.Publisher))];
}
