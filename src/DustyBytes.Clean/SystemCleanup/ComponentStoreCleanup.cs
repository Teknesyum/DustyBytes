using System.Text.RegularExpressions;
using DustyBytes.Core;

namespace DustyBytes.Clean.SystemCleanup;

public sealed class ComponentStoreCleanup : ISystemCleanupTask
{
    public const string AnalyzeArguments = "/Online /Cleanup-Image /AnalyzeComponentStore";
    public const string CleanupArguments = "/Online /Cleanup-Image /StartComponentCleanup";

    public string Id => "component-store";
    public string Name => "Bileşen Deposu (WinSxS)";

    public async Task<SystemCleanupEstimate> EstimateAsync(CancellationToken ct)
    {
        var (exitCode, output) = await ProcessRunner.RunAsync("dism.exe", AnalyzeArguments, null, ct);
        var analysis = ParseAnalysis(output);
        return new SystemCleanupEstimate(Id, analysis.RecoverableBytes, analysis.Recommended,
            exitCode == 0 ? analysis.RawText : $"DISM çıktı kodu {exitCode}");
    }

    public async Task<SystemCleanupResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (DryRun.Enabled)
        {
            progress.Report($"[prova] dism.exe {CleanupArguments}");
            return new SystemCleanupResult(Id, true, "Prova kipi: DISM çalıştırılmadı", 0);
        }

        var (exitCode, _) = await ProcessRunner.RunAsync("dism.exe", CleanupArguments, progress, ct);
        return exitCode == 0
            ? new SystemCleanupResult(Id, true, "Bileşen deposu temizlendi", 0)
            : new SystemCleanupResult(Id, false, $"DISM çıktı kodu {exitCode}", 0);
    }

    public static DismAnalysis ParseAnalysis(string output)
    {
        var sizeMatch = Regex.Match(output, @"Recoverable size\s*:\s*([\d.,]+)\s*(KB|MB|GB|TB)", RegexOptions.IgnoreCase);
        var recommendedMatch = Regex.Match(output, @"Component Store Cleanup Recommended\s*:\s*(Yes|No)", RegexOptions.IgnoreCase);

        long bytes = 0;
        if (sizeMatch.Success)
        {
            var number = double.Parse(sizeMatch.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
            var unit = sizeMatch.Groups[2].Value.ToUpperInvariant();
            var multiplier = unit switch
            {
                "KB" => 1024L,
                "MB" => 1024L * 1024,
                "GB" => 1024L * 1024 * 1024,
                "TB" => 1024L * 1024 * 1024 * 1024,
                _ => 1L,
            };
            bytes = (long)(number * multiplier);
        }

        var recommended = recommendedMatch.Success &&
            recommendedMatch.Groups[1].Value.Equals("Yes", StringComparison.OrdinalIgnoreCase);

        return new DismAnalysis(bytes, recommended, output);
    }
}

public sealed record DismAnalysis(long RecoverableBytes, bool Recommended, string RawText);
