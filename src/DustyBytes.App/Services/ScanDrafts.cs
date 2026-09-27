using DustyBytes.Core.Model;
using DustyBytes.Scan;
using DustyBytes.Units;

namespace DustyBytes.App.Services;

public sealed record ScanDraft(IReadOnlyList<Unit> Units, IReadOnlySet<string> Pending);

sealed class ScanDrafts
{
    readonly EarlyUnits _early;
    readonly UnitContext _context;
    readonly IProgress<ScanDraft> _sink;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    ScanNode? _root;
    int _dirty;

    public ScanDrafts(EarlyUnits early, UnitContext context, IProgress<ScanDraft> sink, TimeSpan period)
    {
        _early = early;
        _context = context;
        _sink = sink;
        _loop = Task.Run(() => LoopAsync(period));
    }

    public IReadOnlyList<string> Priority => [.. _early.Roots.Select(r => r.Path)];

    public void Done(ScanNode node)
    {
        var root = node;
        while (root.Parent is not null)
            root = root.Parent;
        Volatile.Write(ref _root, root);
        _early.Done(node.FullPath);
        Volatile.Write(ref _dirty, 1);
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
            if (Interlocked.Exchange(ref _dirty, 0) == 0 || Volatile.Read(ref _root) is not { } root)
                continue;
            if (Build(root) is not { } draft)
                Volatile.Write(ref _dirty, 1);
            else if (!_stop.IsCancellationRequested)
                _sink.Report(draft);
        }
    }

    ScanDraft? Build(ScanNode root)
    {
        try
        {
            var partial = _context with { ScanResult = new ScanResult { Root = root } };
            var units = EarlyUnits.Build(partial);
            var pending = units.Where(u => !_early.IsSettled(u)).Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            return new ScanDraft(units, pending);
        }
        catch (Exception e) when (e is AggregateException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
