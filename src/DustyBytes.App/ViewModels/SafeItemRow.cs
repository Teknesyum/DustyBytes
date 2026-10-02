using CommunityToolkit.Mvvm.ComponentModel;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed record SafeFileLine(string Path, string Size, string When, string Target, bool CanReveal)
{
    public static SafeFileLine Of(SafeFile file)
    {
        var line = PreviewLine.Of(new PreviewFile(file.Path, Math.Max(0, file.Bytes), file.LastWriteUtc, ""));
        var registry = file.Path.StartsWith(CleanerCatalog.RegistryPrefix, StringComparison.Ordinal);
        return new SafeFileLine(line.Path, file.Bytes < 0 ? "" : line.Size, line.When, file.Path, !registry);
    }
}

public sealed partial class SafeItemRow : ObservableObject
{
    bool _fetched;

    public SafeItemRow(SafeItem item, bool skipped)
    {
        Item = item;
        _isSkipped = skipped;
        _files = [.. item.Files.Select(SafeFileLine.Of)];
    }

    public SafeItem Item { get; }
    public string Key => Item.Key;
    public string Name => Item.Name;
    public string What => Item.What;
    public bool HasWhat => !string.IsNullOrWhiteSpace(Item.What);
    public string SizeText => Item.Count is > 0 and var count ? $"{Format.Count(count)} dosya · {Format.Bytes(Item.Bytes)}" : Format.Bytes(Item.Bytes);
    public bool CanExpand => Item.Kind != SafeItemKind.Task;
    public string ExpandText => IsExpanded ? "Gizle" : Item.Kind == SafeItemKind.Unit ? "Klasörü gör" : "Dosyaları gör";
    public string SkipText => IsSkipped ? "Geri ekle" : "Bunu atla";
    public string SkipTip => IsSkipped ? "Bu kalem yeniden güvenli temizliğe girer" : "Bu kalem bu temizlikte silinmez; toplamdan düşer";
    public string SkippedText => "Bu temizlikte atlanır";
    public bool NeedsFiles => Item.Kind == SafeItemKind.Rule && !_fetched && (Item.Count ?? 0) > Files.Count;
    public bool HasFiles => Files.Count > 0;

    public string FilesNote => IsLoading ? "Dosyalar okunuyor"
        : Files.Count == 0 ? "Dosya listesi alınamadı"
        : Item.Count is { } count && count > Files.Count ? $"{Format.Count(count)} dosyadan en büyük {Format.Count(Files.Count)} tanesi"
        : "";

    public bool HasFilesNote => IsExpanded && FilesNote.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipText), nameof(SkipTip))]
    private bool _isSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandText), nameof(HasFilesNote))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesNote), nameof(HasFilesNote))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles), nameof(FilesNote), nameof(HasFilesNote), nameof(NeedsFiles))]
    private IReadOnlyList<SafeFileLine> _files;

    public void SetFiles(IReadOnlyList<SafeFile>? files)
    {
        _fetched = true;
        if (files is { Count: > 0 } && files.Count >= Files.Count)
            Files = [.. files.Select(SafeFileLine.Of)];
        else
            OnPropertyChanged(nameof(NeedsFiles));
    }
}
