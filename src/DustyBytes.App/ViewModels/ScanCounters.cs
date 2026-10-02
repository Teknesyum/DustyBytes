using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed record ScanCategory(string Title, string Filter, params UnitKind[] Kinds);

public sealed partial class ScanCounter : ObservableObject
{
    readonly Action<ScanCounter> _open;

    public ScanCounter(ScanCategory category, Action<ScanCounter> open)
    {
        Category = category;
        _open = open;
        _text = category.Title + " 0";
    }

    public ScanCategory Category { get; }
    public string Title => Category.Title;
    public string Filter => Category.Filter;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _bytes;

    [ObservableProperty]
    private string _text;

    public string Hint => "Öneriler ekranını bu süzgeçle açar; tarama sürerken de okuyabilirsiniz";

    public void Set(int count, long bytes)
    {
        Count = count;
        Bytes = bytes;
        Text = count == 0 ? Title + " 0" : $"{Title} {Format.Count(count)} · {Format.Bytes(bytes)}";
    }

    [RelayCommand]
    private void Open() => _open(this);
}

public static class ScanCounters
{
    public static IReadOnlyList<ScanCategory> Categories { get; } =
    [
        new("Oyunlar", "Oyun", UnitKind.Game),
        new("Filmler", "Film", UnitKind.Film, UnitKind.Series),
        new("Programlar", "Program", UnitKind.Program),
        new("Önbellek", "Önbellek", UnitKind.Cache, UnitKind.BrowserCache),
        new("Klasörler", "Klasör", UnitKind.Folder),
    ];

    public static IReadOnlyList<(int Count, long Bytes)> Tally(IEnumerable<Unit> units)
    {
        var counts = new int[Categories.Count];
        var bytes = new long[Categories.Count];
        foreach (var unit in units)
        {
            if (unit.SizeBytes < OffersViewModel.SmallBytes)
                continue;
            for (var i = 0; i < Categories.Count; i++)
                if (Array.IndexOf(Categories[i].Kinds, unit.Kind) >= 0)
                {
                    counts[i]++;
                    bytes[i] += unit.SizeBytes;
                    break;
                }
        }
        return [.. counts.Select((c, i) => (c, bytes[i]))];
    }
}
