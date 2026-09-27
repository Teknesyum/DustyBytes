using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Kabuk;
using DustyBytes.App.Services;

namespace DustyBytes.App.ViewModels;

public enum UpdateState
{
    None,
    Available,
    Downloading,
    Ready,
    Installing,
}

public sealed partial class UpdateViewModel : ObservableObject
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    readonly UpdateService _service;
    readonly Func<bool> _busy;
    readonly Func<string, string, string, Task<bool>> _confirm;
    readonly Action<string> _notify;
    readonly Action<string> _fail;
    readonly Action<Action> _post;
    CancellationTokenSource? _loop;
    CancellationTokenSource? _download;
    string? _zip;

    public UpdateViewModel(UpdateService service, Func<bool> busy, Func<string, string, string, Task<bool>> confirm,
        Action<string> notify, Action<string> fail, Action<Action>? post = null)
    {
        _service = service;
        _busy = busy;
        _confirm = confirm;
        _notify = notify;
        _fail = fail;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
    }

    public UpdateService Service => _service;
    public UpdateInfo? Info { get; private set; }
    public Action? Exit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsReady), nameof(Text), nameof(Tip), nameof(Badge), nameof(Panel), nameof(Version))]
    private UpdateState _state;

    [ObservableProperty]
    private bool _isPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private int _percent;

    public bool IsVisible => State != UpdateState.None;
    public bool IsReady => State is UpdateState.Ready or UpdateState.Installing;

    public RozetDurumu Badge => State switch
    {
        UpdateState.Available => RozetDurumu.Var,
        UpdateState.Downloading => RozetDurumu.Iniyor,
        UpdateState.Ready or UpdateState.Installing => RozetDurumu.Hazir,
        _ => RozetDurumu.Yok,
    };

    public GuncellemeDurumu Panel => State switch
    {
        UpdateState.Downloading => GuncellemeDurumu.Iniyor,
        UpdateState.Ready or UpdateState.Installing => GuncellemeDurumu.Hazir,
        _ => GuncellemeDurumu.Var,
    };

    public string Version => VersionText;

    public string Text => State switch
    {
        UpdateState.Available or UpdateState.Ready => Labels.Update,
        UpdateState.Downloading => $"İniyor %{Percent}",
        UpdateState.Installing => "Kuruluyor",
        _ => "",
    };

    public string Tip => State switch
    {
        UpdateState.Available => $"{Labels.UpdateDownload} · DustyBytes {VersionText}",
        UpdateState.Downloading => "Arka planda düşük öncelikle iniyor; tarama ya da silme sürerken yavaşlar. İptal için tıkla.",
        UpdateState.Ready => $"{Labels.UpdateInstall} · DustyBytes {VersionText} doğrulandı",
        UpdateState.Installing => "Program kapanıp yeni sürümle açılacak.",
        _ => "",
    };

    string VersionText => Info?.Version.ToString(3) ?? "";

    public void Start()
    {
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
    }

    public void Stop()
    {
        _loop?.Cancel();
        _download?.Cancel();
    }

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await CheckAsync(ct);
            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (State is not (UpdateState.None or UpdateState.Available))
            return;
        var info = await _service.CheckAsync(ct);
        if (State is not (UpdateState.None or UpdateState.Available))
            return;
        Info = info;
        State = info is null ? UpdateState.None : UpdateState.Available;
        OnPropertyChanged(nameof(Tip));
    }

    [RelayCommand]
    private void TogglePanel()
    {
        if (State == UpdateState.None)
            return;
        IsPanelOpen = !IsPanelOpen;
    }

    [RelayCommand]
    private Task Download() => DownloadAsync();

    [RelayCommand]
    private Task DownloadAndInstall() => DownloadAsync(install: true);

    [RelayCommand]
    private void Cancel() => _download?.Cancel();

    [RelayCommand]
    private Task Install() => InstallAsync();

    [RelayCommand]
    private async Task Act()
    {
        switch (State)
        {
            case UpdateState.Available:
                await DownloadAsync();
                break;
            case UpdateState.Ready:
                await InstallAsync();
                break;
        }
    }

    public async Task DownloadAsync(bool install = false)
    {
        if (State != UpdateState.Available || Info is null)
            return;
        Percent = 0;
        State = UpdateState.Downloading;
        _download = new CancellationTokenSource();
        var result = await _service.DownloadAsync(Info, p => _post(() => Percent = Math.Max(Percent, (int)Math.Floor(p * 100))), _busy, _download.Token);
        if (result.Ok)
        {
            _zip = result.ZipPath;
            Percent = 100;
            State = UpdateState.Ready;
            if (install)
            {
                if (_busy())
                    _notify("Güncelleme indi; süren iş bitince rozetten yükleyebilirsin.");
                else
                    await InstallAsync(ask: false);
            }
            return;
        }
        State = UpdateState.Available;
        if (_download.IsCancellationRequested)
            return;
        _fail(result.Status == DownloadStatus.Failed
            ? "Güncelleme inemedi: " + result.Error
            : "İnen güncelleme doğrulanamadı (" + result.Error + "); kurulmayacak.");
    }

    public async Task InstallAsync(bool ask = true)
    {
        if (State != UpdateState.Ready || Info is null)
            return;
        var ok = !ask || await _confirm("Güncelleme kurulsun mu?",
            $"DustyBytes {VersionText} kurulacak. Program kapanıp yeni sürümle açılacak; açık bir tarama ya da silme varsa önce bitmesini bekle.",
            "Kur ve yeniden başlat");
        if (!ok || State != UpdateState.Ready)
            return;
        State = UpdateState.Installing;
        if (Info.Simulated || _zip is null)
        {
            _notify("Prova kipi: kurulum yapılmadı, program açık kalıyor.");
            State = UpdateState.Ready;
            return;
        }
        try
        {
            if (!_service.Install(Info, _zip))
                throw new InvalidOperationException("yardımcı betik başlatılamadı");
            Exit?.Invoke();
        }
        catch (Exception e)
        {
            State = UpdateState.Ready;
            _fail("Güncelleme kurulamadı: " + e.Message);
        }
    }
}
