using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.Core.Model;
using DustyBytes.Scan;

namespace DustyBytes.App.ViewModels;

public sealed class MapEntry(string name, long bytes, ScanNode? node, UnitKind? kind, IReadOnlyList<MapEntry>? merged = null)
{
    public string Name { get; } = name;
    public long Bytes { get; } = bytes;
    public ScanNode? Node { get; } = node;
    public UnitKind? Kind { get; } = kind;
    public IReadOnlyList<MapEntry> Merged { get; } = merged ?? [];
    public bool IsOthers => Merged.Count > 0;
    public bool CanEnter => Node?.Children is { Count: > 0 };
    public string SizeText => Format.Bytes(Bytes);
}

public sealed record Crumb(string Name, ScanNode Node);

public sealed record LegendItem(string Label, UnitKind? Kind, bool Outline);

public sealed partial class MapViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly Dictionary<string, UnitKind> _kinds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ScanNode, Dictionary<UnitKind, long>> _within = [];

    public MapViewModel(MainViewModel main)
    {
        _main = main;
        main.Session.SnapshotChanged += (_, _) => Load();
        main.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.SelectedDrive))
                Load();
        };
        Load();
    }

    public ObservableCollection<Crumb> Crumbs { get; } = [];

    public IReadOnlyList<LegendItem> Legend { get; } =
    [
        new("Oyun", UnitKind.Game, false),
        new("Film ve dizi", UnitKind.Film, false),
        new("Program", UnitKind.Program, true),
        new("Geliştirici", UnitKind.DevArtifact, false),
        new("Önbellek", UnitKind.Cache, true),
        new("Diğer", null, false),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMap))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    private ScanNode? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    [NotifyPropertyChangedFor(nameof(SelectionSize))]
    [NotifyPropertyChangedFor(nameof(SelectionTail))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MapEntry? _selected;

    [ObservableProperty]
    private IReadOnlyList<MapEntry> _entries = [];

    public bool HasMap => Current is not null;
    public bool IsEmpty => Current is null;
    public bool HasSelection => Selected is not null;

    public string SelectionText => Selected switch
    {
        null => "Bir dilim seçin; çift tıklayınca içine girer",
        { IsOthers: true } s => $"{Format.Count(s.Merged.Count)} küçük öğe · ",
        { } s => $"{s.Name} · ",
    };

    public string SelectionSize => Selected?.SizeText ?? "";

    public string SelectionTail => Selected is { IsOthers: false, Kind: { } k } ? " · " + KindText.Label(k) : "";

    void Load()
    {
        _kinds.Clear();
        _within.Clear();
        var snapshot = _main.Session.Snapshot;
        if (snapshot is null)
        {
            Crumbs.Clear();
            Current = null;
            Entries = [];
            return;
        }
        var result = (_main.Session.SelectedDrive is { } drive ? snapshot.For(drive) : null) ?? snapshot.Result;
        var root = result.Root.Name.TrimEnd('\\') + "\\";
        foreach (var unit in snapshot.Units)
            foreach (var path in unit.Paths)
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!_kinds.TryAdd(path.TrimEnd('\\'), unit.Kind))
                    continue;
                var found = result.Root.Find(path);
                if (found is null)
                    continue;
                var kind = Group(unit.Kind);
                for (var n = found.Parent; n is not null; n = n.Parent)
                {
                    if (!_within.TryGetValue(n, out var sums))
                        _within[n] = sums = [];
                    sums[kind] = sums.GetValueOrDefault(kind) + found.Size;
                }
            }
        Show(result.Root);
    }

    public UnitKind? KindOf(ScanNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (_kinds.TryGetValue(n.FullPath.TrimEnd('\\'), out var kind))
                return kind;
        if (node.Size <= 0 || !_within.TryGetValue(node, out var sums) || sums.Count == 0)
            return null;
        var top = sums.MaxBy(p => p.Value);
        return top.Value * 2 >= node.Size ? top.Key : null;
    }

    static UnitKind Group(UnitKind kind) => kind switch
    {
        UnitKind.Series => UnitKind.Film,
        UnitKind.BrowserCache => UnitKind.Cache,
        UnitKind.AppContent => UnitKind.Program,
        _ => kind,
    };

    void Show(ScanNode node)
    {
        Current = node;
        Selected = null;
        Crumbs.Clear();
        var chain = new Stack<ScanNode>();
        for (var n = node; n is not null; n = n.Parent)
            chain.Push(n);
        foreach (var n in chain)
            Crumbs.Add(new Crumb(n.Parent is null ? n.Name.TrimEnd('\\') : n.Name, n));
        Entries = [.. (node.Children ?? []).Where(c => c.Size > 0).OrderByDescending(c => c.Size).Select(c => new MapEntry(c.Name, c.Size, c, KindOf(c)))];
    }

    public static MapEntry Others(IReadOnlyList<MapEntry> merged) =>
        new($"Diğerleri ({merged.Count})", merged.Sum(m => m.Bytes), null, null, merged);

    [RelayCommand]
    private void Select(MapEntry? entry) => Selected = entry;

    [RelayCommand]
    private void Enter(MapEntry? entry)
    {
        if (entry?.Node is { } node && entry.CanEnter)
            Show(node);
        else
            Selected = entry;
    }

    bool CanUp() => Current?.Parent is not null;

    [RelayCommand(CanExecute = nameof(CanUp))]
    private void Up()
    {
        if (Current?.Parent is { } parent)
            Show(parent);
    }

    [RelayCommand]
    private void Jump(Crumb? crumb)
    {
        if (crumb is not null && !ReferenceEquals(crumb.Node, Current))
            Show(crumb.Node);
    }

    [RelayCommand]
    private void OpenOverview() => _main.GoTo(_main.Overview);
}
