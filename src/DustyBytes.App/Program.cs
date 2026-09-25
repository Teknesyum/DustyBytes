using Avalonia;

namespace DustyBytes.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--worker")
            return Worker.WorkerHost.Run(args[1..]);

        var instance = Worker.SingleInstance.Acquire("DustyBytes");
        if (!instance.IsFirst)
        {
            instance.NotifyFirstAsync(args).GetAwaiter().GetResult();
            instance.Dispose();
            return 0;
        }
        instance.StartListening();
        App.Instance = instance;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
