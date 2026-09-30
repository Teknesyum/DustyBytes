using DustyBytes.Core.Model;
using DustyBytes.Scan;
using DustyBytes.Units;

namespace DustyBytes.App.Services;

public sealed record ScanDraft(IReadOnlyList<Unit> Units, IReadOnlySet<string> Pending);

sealed class ScanDrafts
{
    readonly IReadOnlyList<string> _roots;
    readonly IReadOnlyList<EarlyUnits> _early;
    readonly ScanNode?[] _nodes;
    readonly UnitContext _context;
    readonly IProgress<ScanDraft> _sink;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    int _dirty;

    public ScanDrafts(IReadOnlyList<string> roots, IReadOnlyList<EarlyUnits> early, UnitContext context, IProgress<ScanDraft> sink, TimeSpan period)
    {
        _roots = roots;
        _early = early;
        _nodes = new ScanNode?[roots.Count];
        _context = context;
        _sink = sink;
        _loop = Task.Run(() => LoopAsync(period));
    }

    public IReadOnlyList<string> Priority => [.. _early.SelectMany(e => e.Roots).Select(r => r.Path)];

    public void Done(ScanNode node)
    {
        var root = node;
        while (root.Parent is not null)
            root = root.Parent;
        var index = Index(root.Name);
        if (index < 0)
            return;
        Volatile.Write(ref _nodes[index], root);
        _early[index].Done(node.FullPath);
        Volatile.Write(ref _dirty, 1);
    }

    int Index(string root)
    {
        for (var i = 0; i < _roots.Count; i++)
            if (_roots[i].Equals(root, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }

    async Task LoopAsync(TimeSpan period)
    {
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                continue;
            if (Build() is not { } draft)
                Volatile.Write(ref _dirty, 1);
            else if (!_stop.IsCancellationRequested)
                _sink.Report(draft);
        }
    }

    ScanDraft? Build()
    {
        try
        {
            var parts = new List<IReadOnlyList<Unit>>();
            for (var i = 0; i < _roots.Count; i++)
            {
                var root = Volatile.Read(ref _nodes[i]);
                parts.Add(root is null ? [] : EarlyUnits.Build(_context with { ScanResult = new ScanResult { Root = root }, SystemDrive = i == 0 }));
            }
            var units = UnitBuilder.Merge(_roots, parts);
            var pending = units.Where(u => !Settled(u)).Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            return new ScanDraft(units, pending);
        }
        catch (Exception e) when (e is AggregateException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    bool Settled(Unit unit)
    {
        var owner = UnitBuilder.Owner(_roots, unit);
        return owner < 0 || _early[owner].IsSettled(unit);
    }
}
