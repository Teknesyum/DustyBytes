using System.Runtime.InteropServices;

namespace DustyBytes.App.Services;

public static partial class ReducedMotion
{
    const uint SpiGetClientAreaAnimation = 0x1042;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    public static bool IsOn()
    {
        if (Environment.GetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION") is { Length: > 0 } forced)
            return forced != "0";
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            var enabled = 1;
            return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) && enabled == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
