using DustyBytes.App.Services;

namespace DustyBytes.App.ViewModels;

public sealed partial class MainViewModel
{
    public static TimeSpan LaunchPoll { get; set; } = TimeSpan.FromMilliseconds(100);

    public Func<string, bool> RedeemToken { get; set; } = token => NoticeState.Current?.Redeem(token) ?? false;

    public async Task HandleLaunchAsync(LaunchArgs launch)
    {
        if (launch.Mode != LaunchMode.SafeClean)
            return;
        if (launch.Token is { } token && !RedeemToken(token))
        {
            Notify("Bildirimin süresi dolmuş, genel bakıştan güvenli temizliği başlatabilirsiniz");
            return;
        }
        Olcum.Click("notification-safe-clean", true);
        await SafeCleanWhenReadyAsync();
    }

    public async Task SafeCleanWhenReadyAsync()
    {
        while (Session.Scan.IsRunning || Session.IsRestoring)
            await Task.Delay(LaunchPoll);
        if (!Session.HasSnapshot)
            await Session.RunScanAsync(this, ScanMode.Refresh);
        if (!Session.HasSnapshot)
            return;
        await StartTourAsync(TourMode.Safe);
    }
}
