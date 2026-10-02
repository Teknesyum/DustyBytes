using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const int toastMax = 3;
    public static readonly TimeSpan ToastLife = TimeSpan.FromSeconds(6);

    readonly HashSet<TaskProgressViewModel> _running = [];
    readonly Stack<ConfirmViewModel> _pendingConfirms = new();
    int _busyCount;

    public MainViewModel(IAppBackend backend, string? startScreen = null, UpdateService? updates = null)
    {
        Backend = backend;
        Update = new UpdateViewModel(updates ?? UpdateService.FromEnvironment(), () => IsBusy,
            (title, message, confirm) => ConfirmAsync(title, message, confirm, false), m => Notify(m), m => Fail(m));
        Session = new SessionState(backend);
        Session.Attach(this);
        Sessions = new CleanSession(backend);
        Sessions.Finished += r => Report = new SessionReportViewModel(this, r);
        Navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationStack.Current) && Sessions.Owner is { } owner && !ReferenceEquals(owner, Navigation.Current))
                Sessions.End();
        };
        Overview = new OverviewViewModel(this);
        Offers = new OffersViewModel(this);
        Map = new MapViewModel(this);
        Programs = new ProgramsViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Quarantine = new QuarantineViewModel(this);
        Tour = new TourViewModel(this);
        NavItems =
        [
            new NavItem("Genel bakış", "M3 3h7v7H3z M14 3h7v4h-7z M14 11h7v10h-7z M3 14h7v7H3z", Overview),
            new NavItem("Öneriler", "M12 3l2.6 5.6 6.1.7-4.5 4.2 1.2 6L12 16.6 6.6 19.5l1.2-6L3.3 9.3l6.1-.7z", Offers),
            new NavItem("Harita", "M3 3h11v11H3z M16 3h5v6h-5z M16 11h5v10h-5z M3 16h11v5H3z", Map),
            new NavItem("Programlar", "M4 4h16v12H4z M8 20h8 M12 16v4", Programs),
            new NavItem("Temizlik", "M5 20h14 M7 20l1-9h8l1 9 M10 11V4h4v7", Cleanup),
            new NavItem("Karantina", "M4 7h16v13H4z M9 11h6 M3 4h18v3H3z", Quarantine),
        ];
        var start = NavItems.FirstOrDefault(n => n.Screen.GetType().Name.StartsWith(startScreen ?? "", StringComparison.OrdinalIgnoreCase) && startScreen is not null) ?? NavItems[0];
        _selectedNav = start;
        Navigation.Navigate(start.Screen);
    }

    public IAppBackend Backend { get; }
    public SessionState Session { get; }
    public CleanSession Sessions { get; }
    public NavigationStack Navigation { get; } = new();
    public IReadOnlyList<NavItem> NavItems { get; }
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public OverviewViewModel Overview { get; }
    public OffersViewModel Offers { get; }
    public MapViewModel Map { get; }
    public ProgramsViewModel Programs { get; }
    public CleanupViewModel Cleanup { get; }
    public QuarantineViewModel Quarantine { get; }
    public TourViewModel Tour { get; }

    public async Task StartTourAsync()
    {
        if (!Session.HasSnapshot || Tour.IsActive)
            return;
        if (IsBusy)
        {
            Notify("Başka bir işlem sürüyor; bitince yeniden deneyin");
            return;
        }
        Navigation.Push(Tour);
        await Tour.StartAsync();
    }
    public UpdateViewModel Update { get; }

    public bool IsBusy => Volatile.Read(ref _busyCount) > 0;

    public Func<Uri, Task<bool>>? Launcher { get; set; }

    public string DryRunText => Backend.DryRun ? "Prova kipi: hiçbir dosya silinmez" : "";
    public bool IsDryRun => Backend.DryRun;

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ConfirmViewModel? _confirm;

    [ObservableProperty]
    private SessionReportViewModel? _report;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null)
            Navigation.Navigate(value.Screen);
    }

    public void GoTo(ViewModelBase screen)
    {
        var item = NavItems.FirstOrDefault(n => ReferenceEquals(n.Screen, screen));
        if (item is null)
            return;
        if (ReferenceEquals(SelectedNav, item))
            Navigation.Navigate(screen);
        else
            SelectedNav = item;
    }

    public TaskProgressViewModel NewProgress() => new(Track);

    void Track(TaskProgressViewModel progress, bool running)
    {
        if (running)
            _running.Add(progress);
        else
            _running.Remove(progress);
        Volatile.Write(ref _busyCount, _running.Count);
    }

    public event Action<TimeSpan>? Ticked;

    public void Tick(TimeSpan elapsed)
    {
        Ticked?.Invoke(elapsed);
        foreach (var p in _running.ToList())
            p.Tick();
        foreach (var toast in Toasts.ToList())
            if (toast.Elapse(elapsed))
                Toasts.Remove(toast);
    }

    public ToastViewModel Notify(string message, string? actionText = null, Func<Task>? action = null) => Push(message, false, actionText, action);

    public ToastViewModel Fail(string message, string? actionText = null, Func<Task>? action = null) => Push(message, true, actionText, action);

    ToastViewModel Push(string message, bool error, string? actionText, Func<Task>? action)
    {
        var toast = new ToastViewModel(message, error, actionText, action, ToastLife, t => Toasts.Remove(t));
        Toasts.Add(toast);
        while (Toasts.Count > toastMax)
        {
            var oldest = Toasts.FirstOrDefault(t => !t.IsError) ?? Toasts[0];
            Toasts.Remove(oldest);
        }
        return toast;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = true)
    {
        var dialog = new ConfirmViewModel(title, message, confirmText, danger);
        if (Confirm is not null)
            _pendingConfirms.Push(Confirm);
        Confirm = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Confirm = _pendingConfirms.Count > 0 ? _pendingConfirms.Pop() : null;
        }
    }

    public async Task OpenUri(string uri)
    {
        if (Launcher is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            Fail("Bağlantı açılamadı: " + uri);
            return;
        }
        if (!await Launcher(parsed))
            Fail("Bağlantı açılamadı: " + uri);
    }

    public async Task StartAsync() => await Session.StartAsync(this);

    public void Inspect(string path)
    {
        GoTo(Map);
        Map.Focus(path);
    }

    public static string Size(long bytes) => Format.Bytes(bytes);
}
