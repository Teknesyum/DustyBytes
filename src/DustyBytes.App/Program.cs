using Avalonia;
using DustyBytes.App.Services;

namespace DustyBytes.App;

public static class Program
{
    public static LaunchArgs Launch { get; private set; } = new(LaunchMode.Normal);

    [STAThread]
    public static int Main(string[] args)
    {
        Launch = LaunchArgs.Parse(args);
        switch (Launch.Mode)
        {
            case LaunchMode.Worker:
                return Worker.WorkerHost.Run(args[1..]);
            case LaunchMode.Check:
                if (!Core.AppSettings.Load().WeeklyCheck)
                    return 0;
                var integration = SystemIntegration.ForCurrentUser();
                return DiskCheck.Run(DiskCheck.FixedDrives(), new WindowsToast(integration.Shell), root => GrowthText.WeeklyFor(root, DateTimeOffset.Now), NoticeState.ForCurrentUser());
            case LaunchMode.Snooze:
                return NoticeState.ForCurrentUser().Snooze(Launch.Token) ? 0 : 1;
            case LaunchMode.Mute:
                return NoticeActions.Mute(NoticeState.ForCurrentUser(), Launch.Token, NoticeActions.DisableWeekly) ? 0 : 1;
            case LaunchMode.Unregister:
                return SystemIntegration.ForCurrentUser().Unregister().Error is null ? 0 : 1;
        }

        var instance = Worker.SingleInstance.Acquire("DustyBytes");
        if (!instance.IsFirst)
        {
            instance.NotifyFirstAsync(args).GetAwaiter().GetResult();
            instance.Dispose();
            return 0;
        }
        instance.StartListening();
        App.Instance = instance;
        NoticeState.Current = NoticeState.ForCurrentUser();
        Olcum.Current = Olcum.Begin();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
