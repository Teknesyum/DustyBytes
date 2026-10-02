using System.Collections.ObjectModel;
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

public sealed partial class SafeUnitRow(SafeUnit unit, SafeItemRow group, bool skipped) : ObservableObject
{
    public SafeUnit Unit { get; } = unit;
    public SafeItemRow Group { get; } = group;
    public string Key => Unit.Key;
    public string Name => Unit.Name;
    public string Place => Unit.Place;
    public string Target => Unit.Target;
    public string SizeText => Format.Bytes(Unit.Bytes);
    public bool CanReveal => Path.IsPathFullyQualified(Unit.Target);
    public string SkipText => IsSkipped ? "Geri ekle" : "Bunu atla";
    public string SkipTip => IsSkipped ? "Bu klasör yeniden güvenli temizliğe girer" : "Yalnız bu klasör bu temizlikte silinmez; toplamdan düşer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipText), nameof(SkipTip))]
    private bool _isSkipped = skipped;
}

public sealed partial class SafeItemRow : ObservableObject
{
    bool _fetched;
    IReadOnlySet<string> _skips;

    public SafeItemRow(SafeItem item, IReadOnlySet<string> skips)
    {
        Item = item;
        _skips = skips;
        _isSkipped = IsGroup ? item.Units.Count > 0 && item.Units.All(u => skips.Contains(u.Key)) : skips.Contains(item.Key);
        _files = [.. item.Files.Select(SafeFileLine.Of)];
    }

    public bool IsGroup => Item.Kind == SafeItemKind.Group;
    public ObservableCollection<SafeUnitRow> Units { get; } = [];
    public int SkippedUnits => IsGroup ? Item.Units.Count(u => _skips.Contains(u.Key)) : 0;
    public bool HasUnits => IsGroup && Units.Count > 0;
    public int RemainingUnits => IsGroup ? Item.Units.Count - Units.Count : 0;
    public bool HasMoreUnits => IsExpanded && RemainingUnits > 0;
    public string MoreUnitsText => $"{Format.Count(Math.Min(SafeBreakdown.UnitPage, RemainingUnits))} tane daha göster ({Format.Count(RemainingUnits)} kaldı)";
    public bool HasSkipNote => IsSkipped || SkippedUnits > 0;

    public void ShowMoreUnits()
    {
        foreach (var unit in Item.Units.Skip(Units.Count).Take(SafeBreakdown.UnitPage))
            Units.Add(new SafeUnitRow(unit, this, _skips.Contains(unit.Key)));
        OnPropertyChanged(nameof(HasUnits));
        OnPropertyChanged(nameof(RemainingUnits));
        OnPropertyChanged(nameof(HasMoreUnits));
        OnPropertyChanged(nameof(MoreUnitsText));
    }

    public void Sync(IReadOnlySet<string> skips)
    {
        _skips = skips;
        foreach (var unit in Units)
            unit.IsSkipped = skips.Contains(unit.Key);
        IsSkipped = IsGroup ? Item.Units.Count > 0 && Item.Units.All(u => skips.Contains(u.Key)) : skips.Contains(Item.Key);
        OnPropertyChanged(nameof(SkippedUnits));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SkippedText));
        OnPropertyChanged(nameof(HasSkipNote));
    }

    public SafeItem Item { get; }
    public string Key => Item.Key;
    public string Name => Item.Name;
    public string What => Item.What;
    public bool HasWhat => !string.IsNullOrWhiteSpace(Item.What);
    public string SizeText => IsGroup ? Format.Bytes(SafeBreakdown.Active(Item, _skips))
        : Item.Count is > 0 and var count ? $"{Format.Count(count)} dosya · {Format.Bytes(Item.Bytes)}" : Format.Bytes(Item.Bytes);
    public bool CanExpand => Item.Kind != SafeItemKind.Task;
    public string ExpandText => IsExpanded ? "Gizle" : IsGroup ? "Klasörleri gör" : "Dosyaları gör";
    public string SkipText => IsSkipped ? "Geri ekle" : IsGroup ? "Hepsini atla" : "Bunu atla";
    public string SkipTip => IsSkipped ? "Bu kalem yeniden güvenli temizliğe girer"
        : IsGroup ? "Bu gruptaki klasörlerin hiçbiri bu temizlikte silinmez; toplamdan düşer"
        : "Bu kalem bu temizlikte silinmez; toplamdan düşer";
    public string SkippedText => !IsSkipped && SkippedUnits > 0 ? $"{Format.Count(SkippedUnits)} {Item.Noun} bu temizlikte atlanır" : "Bu temizlikte atlanır";
    public bool NeedsFiles => Item.Kind == SafeItemKind.Rule && !_fetched && (Item.Count ?? 0) > Files.Count;
    public bool HasFiles => !IsGroup && Files.Count > 0;

    public string FilesNote => IsGroup ? ""
        : IsLoading ? "Dosyalar okunuyor"
        : Files.Count == 0 ? "Dosya listesi alınamadı"
        : Item.Count is { } count && count > Files.Count ? $"{Format.Count(count)} dosyadan en büyük {Format.Count(Files.Count)} tanesi"
        : "";

    public bool HasFilesNote => IsExpanded && FilesNote.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipText), nameof(SkipTip), nameof(SkippedText), nameof(HasSkipNote))]
    private bool _isSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandText), nameof(HasFilesNote), nameof(HasMoreUnits))]
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
