using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
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
    public string OpenText => File.Kind == FileKind.Video ? "Oynat" : "Aç";
    public string OpenHint => "Varsayılan uygulamada açılır";
    public string RevealHint => "Dosyayı kendi klasöründe seçili gösterir";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumb), nameof(NoThumb))]
    private Bitmap? _thumb;

    public bool HasThumb => Thumb is not null;
    public bool NoThumb => Thumb is null;

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void Reveal() => owner.Reveal(this);
}

public sealed partial class UnitContentsViewModel : ObservableObject
{
    public const int ThumbMax = 12;
    public const int ThumbWidth = 112;
    const long ThumbBytesMax = 40L * 1024 * 1024;

    readonly IReadOnlyList<string> _paths;
    Task<ContentsResult>? _load;

    public UnitContentsViewModel(Unit unit)
    {
        _paths = [.. unit.Paths];
        Kind = unit.Kind;
    }

    public static bool Supports(Unit unit) =>
        unit.Paths.Count > 0 && unit.Kind is UnitKind.Film or UnitKind.Series or UnitKind.Folder or UnitKind.Game or UnitKind.Program;

    public UnitKind Kind { get; }
    public bool CanPlay => Kind == UnitKind.Film;
    public string PlayHint => "Klasördeki en büyük videoyu varsayılan oynatıcıda açar";
    public string ToggleHint => "En büyük dosyaları gösterir; resimlerin küçük önizlemesi çıkar";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<ContentRow> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool HasRows => Rows.Count > 0;
    public bool HasSummary => Summary.Length > 0;
    public bool HasStatus => Status.Length > 0;
    public string ToggleText => IsExpanded ? "İçindekileri gizle" : "İçindekiler";

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
        Rows = [.. result.Files.Select(f => new ContentRow(this, f))];
        Summary = result.FileCount == 0
            ? "Dosya bulunamadı ya da okunamadı"
            : result.FileCount <= Rows.Count
                ? $"{Format.Count(result.FileCount)} dosya, {Format.Bytes(result.TotalBytes)}"
                : $"{Format.Count(result.FileCount)} dosyanın en büyük {Format.Count(Rows.Count)} tanesi, toplam {Format.Bytes(result.TotalBytes)}"
                    + (result.Partial ? "; liste yarıda kesildi" : "");
        _ = ThumbsAsync(Rows);
        return result;
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
