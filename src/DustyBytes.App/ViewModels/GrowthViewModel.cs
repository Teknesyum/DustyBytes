using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Scan;

namespace DustyBytes.App.ViewModels;

public sealed record GrowthRow(string Name, string Path, string SizeText);

public sealed partial class GrowthViewModel : ObservableObject
{
    readonly MainViewModel _main;
    IReadOnlyList<ScanResult> _seen = [];
    int _version;

    public GrowthViewModel(MainViewModel main)
    {
        _main = main;
        main.Session.SnapshotChanged += (_, _) => Update();
        main.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.SelectedDrive))
                Update(true);
        };
        Update();
    }

    public ObservableCollection<GrowthRow> Rows { get; } = [];

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool _hasGrowth;

    [ObservableProperty]
    private string _title = "";

    [RelayCommand]
    private void Inspect(GrowthRow? row)
    {
        if (row is not null)
            _main.Inspect(row.Path);
    }

    void Update(bool force = false)
    {
        var results = Wanted();
        if (!force && results.SequenceEqual(_seen, ReferenceEqualityComparer.Instance))
            return;
        _seen = results;
        Loading = Load(results, ++_version);
    }

    IReadOnlyList<ScanResult> Wanted()
    {
        var snapshot = _main.Session.Snapshot;
        if (snapshot is null)
            return [];
        return _main.Session.SelectedDrive is { } drive && snapshot.For(drive) is { } picked ? [picked] : snapshot.Results;
    }

    async Task Load(IReadOnlyList<ScanResult> results, int version)
    {
        FolderGrowth? growth = null;
        if (results.Count > 0)
        {
            try
            {
                var parts = await Task.WhenAll(results.Select(r => _main.Backend.GrowthAsync(r, CancellationToken.None)));
                growth = FolderHistory.Merge(parts);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                growth = null;
            }
        }
        if (version != _version)
            return;
        Rows.Clear();
        if (growth is null)
        {
            Title = "";
            HasGrowth = false;
            return;
        }
        foreach (var item in growth.Top)
            Rows.Add(new GrowthRow(item.Name, item.Path, GrowthText.Signed(item.Bytes)));
        Title = GrowthText.Title(growth);
        HasGrowth = true;
    }
}
