using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Clean.Safety;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class ContentRow(UnitContentsViewModel owner, ContentFile file) : ObservableObject
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public ContentFile File { get; } = file;
    public string Name => File.Name;
    public string Path => File.Path;
    public string SizeText => Format.Bytes(File.SizeBytes);
    public string DateText => File.LastWrite.ToString("d MMM yyyy", Tr);
    public string KindText => File.Kind switch
    {
        FileKind.Video => "Video",
        FileKind.Audio => "Ses",
        FileKind.Image => "Resim",
        _ => "Dosya",
    };
    public bool CanOpen => SafeOpen.Opens(File.Kind);
    public bool IsImage => File.Kind == FileKind.Image;
    public string OpenText => File.Kind == FileKind.Video || File.Kind == FileKind.Audio ? "Oynat" : "Aç";
    public string OpenHint => "Varsayılan uygulamada açılır";
    public string RevealHint => "Dosyayı kendi klasöründe seçili gösterir";
    public bool CanSelect => owner.CanSelect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumb), nameof(NoThumb))]
    private Bitmap? _thumb;

    public bool HasThumb => Thumb is not null;
    public bool NoThumb => Thumb is null;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => owner.SelectionChanged();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    private string _failure = "";

    public bool HasFailure => Failure.Length > 0;

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void Reveal() => owner.Reveal(this);
}

public sealed record ContentsRemoval(IReadOnlyList<string> Paths, long Bytes, bool Purged, long FreedBytes, long PendingBytes, bool Empty);

public sealed partial class UnitContentsViewModel : ObservableObject
{
    public const int ThumbMax = 12;
    public const int ThumbWidth = 112;
    public const int PageSize = 50;
    const long ThumbBytesMax = 40L * 1024 * 1024;

    readonly IReadOnlyList<string> _paths;
    readonly RemovalMethod _removal;
    readonly List<ContentFile> _files = [];
    Task<ContentsResult>? _load;
    IAppBackend? _backend;
    TwoStep _danger = new();
    Action<ContentsRemoval>? _removed;
    long _fileCount;
    long _totalBytes;
    bool _partial;
    bool _quiet;

    public UnitContentsViewModel(Unit unit)
    {
        _paths = [.. unit.Paths];
        _removal = unit.Removal;
        UnitId = unit.Id;
        Kind = unit.Kind;
    }

    public static bool Supports(Unit unit) =>
        unit.Paths.Count > 0 && unit.Kind is UnitKind.Film or UnitKind.Series or UnitKind.Folder or UnitKind.Game or UnitKind.Program;

    public void Connect(IAppBackend backend, TwoStep danger, Action<ContentsRemoval> removed)
    {
        _backend = backend;
        _danger = danger;
        _removed = removed;
        OnPropertyChanged(nameof(CanSelect));
    }

    public string UnitId { get; }
    public UnitKind Kind { get; }
    public IReadOnlyList<string> Roots => _paths;
    public bool CanPlay => Kind == UnitKind.Film;
    public bool CanSelect => _backend is not null && _removal == RemovalMethod.Quarantine;
    public string PlayHint => "Klasördeki en büyük videoyu varsayılan oynatıcıda açar";
    public string ToggleHint => "Tüm dosyaları büyükten küçüğe gösterir; seçtiklerinizi karantinaya alabilirsiniz";
    public string SelectAllHint => "Listede görünen dosyaların hepsini seçer";
    public string QuarantineHint => $"Seçilen dosyalar karantinaya taşınır; {AppSettings.QuarantineDays.Days} gün içinde geri alırsınız";
    public string PurgeHint => "Karantinaya almadan siler; geri alınamaz";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyCanExecuteChangedFor(nameof(QuarantineSelectedCommand), nameof(PurgeSelectedCommand))]
    private bool _isBusy;

    public ObservableCollection<ContentRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanAct), nameof(SelectionText), nameof(QuarantineText))]
    [NotifyCanExecuteChangedFor(nameof(QuarantineSelectedCommand), nameof(PurgeSelectedCommand))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText), nameof(QuarantineText))]
    private long _selectedBytes;

    public bool HasRows => Rows.Count > 0;
    public bool HasSummary => Summary.Length > 0;
    public bool HasStatus => Status.Length > 0;
    public bool HasNotice => Notice.Length > 0;
    public string DisabledTip => "Önce en az bir dosya seçin";
    public bool HasSelection => SelectedCount > 0;
    public bool CanAct => HasSelection && !IsBusy;
    public bool ShowSelection => CanSelect && HasRows;
    public string ToggleText => IsExpanded ? "İçindekileri gizle" : "İçindekiler";
    public long FileCount => _fileCount;
    public long TotalBytes => _totalBytes;
    public int Remaining => _files.Count - Rows.Count;
    public bool HasMore => Remaining > 0;
    public string MoreText => $"Daha fazla göster ({Format.Count(Remaining)} dosya kaldı)";
    public string SelectionText => SelectedCount == 0
        ? "Hiçbir dosya seçilmedi"
        : $"{Format.Count(SelectedCount)} dosya seçildi, {Format.Bytes(SelectedBytes)}";
    public string QuarantineText => SelectedCount == 0
        ? "Seçilenleri karantinaya al"
        : $"Seçilenleri karantinaya al ({Format.Count(SelectedCount)} dosya, {Format.Bytes(SelectedBytes)})";
    public bool IsPurgeArmed => _danger.IsArmedFor(this);
    public string PurgeText => IsPurgeArmed ? TwoStep.ArmedText : "Seçilenleri kalıcı sil";

    public Task Ready => _load ?? Task.CompletedTask;

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded)
            _ = EnsureAsync();
    }

    public Task<ContentsResult> EnsureAsync()
    {
        if (_load is not null)
            return _load;
        _load = LoadAsync();
        return _load;
    }

    async Task<ContentsResult> LoadAsync()
    {
        IsLoading = true;
        Status = "";
        Summary = "Dosyalar listeleniyor";
        var paths = _paths;
        ContentsResult result;
        try
        {
            result = await Task.Run(() => UnitContents.List(paths));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            result = new ContentsResult([], 0, 0, true);
        }
        IsLoading = false;
        _files.Clear();
        _files.AddRange(result.Files);
        _fileCount = result.FileCount;
        _totalBytes = result.TotalBytes;
        _partial = result.Partial;
        Rows.Clear();
        AddPage();
        UpdateSummary();
        return result;
    }

    void AddPage()
    {
        var page = _files.Skip(Rows.Count).Take(PageSize).Select(f => new ContentRow(this, f)).ToList();
        foreach (var row in page)
            Rows.Add(row);
        RaiseList();
        _ = ThumbsAsync(page);
    }

    void RaiseList()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowSelection));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreText));
    }

    void UpdateSummary()
    {
        var listed = _files.Count;
        Summary = _fileCount == 0
            ? "Dosya bulunamadı ya da okunamadı"
            : _partial
                ? $"İlk {Format.Count(listed)} dosya gösteriliyor, {Format.Bytes(_totalBytes)}; liste süre sınırında kesildi"
                : _fileCount > listed
                    ? $"{Format.Count(_fileCount)} dosyanın en büyük {Format.Count(listed)} tanesi, toplam {Format.Bytes(_totalBytes)}"
                    : $"{Format.Count(_fileCount)} dosya, {Format.Bytes(_totalBytes)}";
    }

    [RelayCommand]
    private void ShowMore()
    {
        if (HasMore)
            AddPage();
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    void SetAll(bool value)
    {
        if (!CanSelect)
            return;
        _quiet = true;
        foreach (var row in Rows)
            row.IsSelected = value;
        _quiet = false;
        SelectionChanged();
    }

    internal void SelectionChanged()
    {
        if (_quiet)
            return;
        var chosen = Rows.Where(r => r.IsSelected).ToList();
        SelectedCount = chosen.Count;
        SelectedBytes = chosen.Sum(r => r.File.SizeBytes);
        if (IsPurgeArmed)
            _danger.Reset();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task QuarantineSelected() => RemoveAsync(purge: false);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task PurgeSelected()
    {
        if (!CanAct)
            return Task.CompletedTask;
        var armed = IsPurgeArmed;
        if (!_danger.Press(this))
        {
            if (!armed)
                _danger.PropertyChanged += OnDanger;
            RaiseArmed();
            return Task.CompletedTask;
        }
        RaiseArmed();
        return RemoveAsync(purge: true);
    }

    void OnDanger(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TwoStep.Target))
            return;
        if (!_danger.IsArmedFor(this))
            _danger.PropertyChanged -= OnDanger;
        RaiseArmed();
    }

    void RaiseArmed()
    {
        OnPropertyChanged(nameof(IsPurgeArmed));
        OnPropertyChanged(nameof(PurgeText));
    }

    async Task RemoveAsync(bool purge)
    {
        var chosen = Rows.Where(r => r.IsSelected).ToList();
        if (chosen.Count == 0 || _backend is not { } backend || !CanSelect || IsBusy)
            return;
        IsBusy = true;
        Status = "";
        Notice = purge ? "Seçilen dosyalar kalıcı siliniyor" : "Seçilen dosyalar karantinaya alınıyor";
        foreach (var row in chosen)
            row.Failure = "";
        WorkerResponse response;
        try
        {
            response = await backend.SendAsync(Request(chosen, purge), new Progress<TaskStep>(), CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or Worker.WorkerStartException)
        {
            IsBusy = false;
            Notice = "";
            Status = "İşlem yapılamadı: " + e.Message;
            return;
        }
        IsBusy = false;
        Apply(chosen, response, purge);
    }

    public WorkerRequest Request(IReadOnlyList<ContentRow> chosen, bool purge) => new()
    {
        Op = purge ? Ops.Delete : Ops.Quarantine,
        Paths = [.. chosen.Select(r => r.Path)],
        Roots = [.. _paths],
        UnitId = UnitId,
        UserApproved = true,
        IncludeUserData = true,
    };

    public void Apply(IReadOnlyList<ContentRow> chosen, WorkerResponse response, bool purge)
    {
        var bytes = chosen.Sum(r => r.File.SizeBytes);
        if (response.DryRun)
        {
            Notice = $"Prova: {Format.Count(chosen.Count)} dosya, {Format.Bytes(bytes)} {(purge ? "kalıcı silinecekti" : "karantinaya alınacaktı")}; dosyalar yerinde";
            return;
        }
        var results = new Dictionary<string, ItemResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in response.Items)
            results[Key(item.Path)] = item;
        var done = new List<ContentRow>();
        foreach (var row in chosen)
        {
            if (results.TryGetValue(Key(row.Path), out var item) && item.Ok)
                done.Add(row);
            else
                row.Failure = item is null ? Fallback(response) : Reason(item.Message);
        }
        var failed = chosen.Count - done.Count;
        var freed = done.Sum(r => r.File.SizeBytes);
        if (done.Count > 0)
        {
            var gone = done.Select(r => r.File).ToHashSet();
            _quiet = true;
            foreach (var row in done)
            {
                row.IsSelected = false;
                Rows.Remove(row);
            }
            _quiet = false;
            _files.RemoveAll(gone.Contains);
            _fileCount = Math.Max(0, _fileCount - done.Count);
            _totalBytes = Math.Max(0, _totalBytes - freed);
            RaiseList();
            UpdateSummary();
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(TotalBytes));
        }
        SelectionChanged();
        Notice = done.Count == 0
            ? ""
            : purge
                ? $"{Format.Count(done.Count)} dosya kalıcı silindi, {Format.Bytes(freed)} yer açıldı."
                : $"{Format.Count(done.Count)} dosya karantinada, {Format.Bytes(freed)} yer açıldı. {AppSettings.QuarantineDays.Days} gün içinde karantina ekranından geri alırsınız.";
        Status = failed > 0 ? $"{Format.Count(failed)} dosya işlenemedi; sebebi dosyanın altında yazıyor." : "";
        if (done.Count > 0)
            _removed?.Invoke(new ContentsRemoval([.. done.Select(r => r.Path)], freed, purge, response.FreedBytes, response.PendingBytes, _fileCount == 0 && !_partial));
    }

    static string Key(string path)
    {
        var at = path.LastIndexOf('|');
        var plain = at >= 0 ? path[..at] : path;
        try
        {
            return Paths.Normalize(plain);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return plain;
        }
    }

    static string Fallback(WorkerResponse response) =>
        response.Message.Length > 0 && !response.Ok ? response.Message : "Worker bu dosya için sonuç bildirmedi";

    public static string Reason(string message)
    {
        var at = message.IndexOf(": ", StringComparison.Ordinal);
        var head = at > 0 ? message[..at] : message;
        var text = at > 0 ? message[(at + 2)..].Trim() : "";
        if (!Enum.TryParse<OpStatus>(head, out var status))
            return message;
        if (text.Length > 0)
            return text;
        return status switch
        {
            OpStatus.Locked => "Dosya kullanımda",
            OpStatus.NotFound => "Dosya bulunamadı",
            OpStatus.Denied => "Korumalı liste izin vermedi",
            OpStatus.Conflict => "Hedefte aynı adlı öğe var",
            _ => "İşlem başarısız",
        };
    }

    static async Task ThumbsAsync(IReadOnlyList<ContentRow> rows)
    {
        foreach (var row in rows.Where(r => r.IsImage && r.File.SizeBytes <= ThumbBytesMax).Take(ThumbMax))
        {
            var path = row.Path;
            var bitmap = await Task.Run(() => Decode(path));
            if (bitmap is not null)
                row.Thumb = bitmap;
        }
    }

    static Bitmap? Decode(string path)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, ThumbWidth, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task Play()
    {
        var result = await EnsureAsync();
        if (UnitContents.MainVideo(result.Files) is not { } video)
        {
            Status = "Oynatılacak video bulunamadı";
            return;
        }
        Status = SafeOpen.Open(video) ?? "";
    }

    internal void Open(ContentRow row) => Status = SafeOpen.Open(row.Path) ?? "";

    internal void Reveal(ContentRow row) => Status = SafeOpen.Reveal(row.Path) ?? "";
}
