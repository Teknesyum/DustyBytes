using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App;

public partial class MainWindow : Window
{
    public const double SidebarWidth = 240;
    public const double ModalWidth = 560;
    public const double Gutter = 32;

    public static Func<MainViewModel?>? ContextFactory { get; set; }

    readonly DispatcherTimer _ticker;
    readonly DispatcherTimer _saveTimer;
    DateTime _lastTick = DateTime.UtcNow;
    WindowStateStore? _store;

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = Token("SidebarWidth", SidebarWidth) + Token("ModalWidth", ModalWidth) + 2 * Token("Space5", Gutter);

        if (!ReducedMotion.IsOn())
            Classes.Add("anim");
        ApplyMotion();
        var inset = Token("ToastInset", 24);
        Toasts.Margin = new Thickness(inset);
        UpdatePanel.Margin = new Thickness(0, Token("TitleBarHeightMax", 40), inset, 0);
        KeyDown += OnWindowKeyDown;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _ticker.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastTick;
            _lastTick = now;
            (DataContext as MainViewModel)?.Tick(elapsed);
        };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Token("TSlow", 400)) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePlacement();
        };

        Opened += (_, _) =>
        {
            _lastTick = DateTime.UtcNow;
            _ticker.Start();
        };
        Closed += (_, _) => _ticker.Stop();
        Closing += (_, _) =>
        {
            _saveTimer.Stop();
            SavePlacement();
        };
        PositionChanged += (_, _) => QueueSave();

        if (ContextFactory?.Invoke() is { } context)
            DataContext = context;
        TraceTabSwitch();
    }

    void TraceTabSwitch()
    {
        if (Environment.GetEnvironmentVariable("DUSTYBYTES_PERF_LOG") is not { Length: > 0 } log)
            return;
        Nav.SelectionChanged += (_, _) =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var label = (Nav.SelectedItem as NavItem)?.Label ?? "?";
            RequestAnimationFrame(_ => RequestAnimationFrame(_ =>
                File.AppendAllText(log, $"{label}\t{watch.Elapsed.TotalMilliseconds:F1}{Environment.NewLine}")));
        };
    }

    double Token(string key, double fallback)
    {
        if (!this.TryFindResource(key, out var value))
            return fallback;
        return value switch
        {
            double d => d,
            Thickness t => t.Left,
            TimeSpan ts => ts.TotalMilliseconds,
            _ => fallback,
        };
    }

    void ApplyMotion()
    {
        if (!Classes.Contains("anim"))
        {
            Pages.PageTransition = null;
            return;
        }
        var duration = this.TryFindResource("TBase", out var value) && value is TimeSpan ts ? ts : TimeSpan.FromMilliseconds(200);
        Pages.PageTransition = new CrossFade(duration);
    }

    public void AttachStore(WindowStateStore store)
    {
        _store = store;
        var placement = store.Load();
        var screens = Screens.All.Select(s => (X: (double)s.WorkingArea.X, Y: (double)s.WorkingArea.Y, Width: (double)s.WorkingArea.Width, Height: (double)s.WorkingArea.Height)).ToList();
        if (placement is not null && (screens.Count == 0 || WindowStateStore.IsVisible(placement, screens)))
        {
            Width = Math.Max(placement.Width, MinWidth);
            Height = Math.Max(placement.Height, MinHeight);
            Position = new PixelPoint((int)placement.X, (int)placement.Y);
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (placement.Maximized)
                WindowState = WindowState.Maximized;
        }
        else
        {
            if (placement is not null)
            {
                Width = Math.Max(placement.Width, MinWidth);
                Height = Math.Max(placement.Height, MinHeight);
            }
            if (Screens.Primary is { } primary)
            {
                var scale = primary.Scaling > 0 ? primary.Scaling : 1;
                Width = Math.Max(MinWidth, Math.Min(Width, primary.WorkingArea.Width / scale));
                Height = Math.Max(MinHeight, Math.Min(Height, primary.WorkingArea.Height / scale));
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty || change.Property == WindowStateProperty)
            QueueSave();
    }

    void QueueSave()
    {
        if (_store is null || _saveTimer is null)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SavePlacement()
    {
        if (_store is null || WindowState == WindowState.Minimized)
            return;
        var maximized = WindowState == WindowState.Maximized;
        _store.Save(new WindowPlacement(Position.X, Position.Y, Bounds.Width, Bounds.Height, maximized));
    }

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel { Update.IsPanelOpen: true } vm)
        {
            vm.Update.IsPanelOpen = false;
            e.Handled = true;
        }
    }

    void OnUpdateLayerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Update.IsPanelOpen = false;
    }

    public void BringForward()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (!IsVisible)
            Show();
        Activate();
        Topmost = true;
        Topmost = false;
    }

    void OnToastEnter(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is ToastViewModel toast)
            toast.IsPaused = true;
    }

    void OnToastExit(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is ToastViewModel toast)
            toast.IsPaused = false;
    }
}
