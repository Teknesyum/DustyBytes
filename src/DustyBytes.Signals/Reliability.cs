namespace DustyBytes.Signals;

public static class Reliability
{
    public const double LauncherLastPlayed = 0.95;
    public const double Prefetch = 0.85;
    public const double UserAssist = 0.75;
    public const double MediaPlayer = 0.6;
    public const double JumpList = 0.5;
    public const double LastWrite = 0.3;
}

public static class SourceNames
{
    public const string SteamLastPlayed = "Steam son oynanma";
    public const string Prefetch = "Prefetch";
    public const string UserAssist = "UserAssist";
    public const string Vlc = "VLC";
    public const string MpcHc = "MPC-HC";
}
