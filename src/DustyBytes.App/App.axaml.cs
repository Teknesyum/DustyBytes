using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App;

public partial class App : Application
{
    public static Worker.SingleInstance? Instance { get; set; }

    public override void Initialize()
    {
        Controls.TransformAnimator.Register();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var backend = new AppBackend();
            var vm = new MainViewModel(backend);
            var window = new MainWindow { DataContext = vm };
            vm.Launcher = async uri => await window.Launcher.LaunchUriAsync(uri);
            window.AttachStore(new WindowStateStore());
            desktop.MainWindow = window;

            if (Instance is not null)
                Instance.Activated += (_, _) => Dispatcher.UIThread.Post(window.BringForward);

            window.Opened += async (_, _) => await vm.StartAsync();
            desktop.Exit += (_, _) =>
            {
                Instance?.Dispose();
                backend.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
