namespace DustyBytes.Core;

public static class DryRun
{
    public const string Variable = "DUSTYBYTES_DRYRUN";

    public static bool Enabled =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } v && v != "0";
}
