using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Uninstall;

public static partial class SharedRuntimes
{
    public const string Note = "Başka programlar bunu kullanıyor olabilir";

    [GeneratedRegex(@"^(Microsoft\s+)?\.NET\b|^Microsoft\s+Windows\s+Desktop\s+Runtime|^Microsoft\s+ASP\.NET\s+Core|\bASP\.NET\s+Core\s+(Runtime|Hosting)|^Microsoft\s+\.NET\s+Framework", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DotNetName();

    [GeneratedRegex(@"Visual\s+C\+\+\s+.*(Redistributable|Runtime)|^Microsoft\s+Visual\s+C\+\+\s+(20\d\d|\d+\.\d+)|^VC\s*\+\+\s*.*Redistributable", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VcRedistName();

    [GeneratedRegex(@"^(Microsoft\s+)?DirectX\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectXName();

    [GeneratedRegex(@"^Java\b|\bJava\s*\(TM\)|\b(JRE|JDK)\b|\bOpenJDK\b|\bTemurin\b|\bCorretto\b|^Zulu\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaName();

    [GeneratedRegex(@"Oracle|Eclipse|Adoptium|Azul|Amazon|BellSoft|Microsoft|OpenJDK|Red\s*Hat|SAP|Java", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaPublisher();

    [GeneratedRegex(@"WebView2\s+Runtime|Windows\s+SDK|Windows\s+Software\s+Development\s+Kit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OtherName();

    public static bool IsShared(InstalledProgram program)
    {
        if (program.IsFramework)
            return true;
        return IsShared(program.DisplayName, program.Publisher);
    }

    public static bool IsShared(string? name, string? publisher)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var trimmed = name.Trim();
        if (DotNetName().IsMatch(trimmed) || VcRedistName().IsMatch(trimmed) || DirectXName().IsMatch(trimmed) || OtherName().IsMatch(trimmed))
            return true;
        return JavaName().IsMatch(trimmed) && (string.IsNullOrWhiteSpace(publisher) || JavaPublisher().IsMatch(publisher));
    }
}
