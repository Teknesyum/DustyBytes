using DustyBytes.Core.Model;

namespace DustyBytes.App.Services;

public sealed record SessionReport
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public long FreeBefore { get; init; }
    public long FreeAfter { get; init; }
    public long QuarantinedBytes { get; init; }
    public int QuarantinedCount { get; init; }
    public long PurgedBytes { get; init; }
    public int PurgedCount { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<Unit> Units { get; init; } = [];

    public bool IsEmpty => QuarantinedCount == 0 && PurgedCount == 0 && PurgedBytes == 0;
    public bool CanUndo => QuarantinedCount > 0 && !DryRun;
    public bool HasQuarantined => QuarantinedCount > 0;
    public bool HasPurged => PurgedCount > 0 || PurgedBytes > 0;
    public string SpaceText => $"Önce {Format.Bytes(FreeBefore)} boş → şimdi {Format.Bytes(FreeAfter)} boş";
    public string QuarantineText => $"Karantinada {Format.Bytes(QuarantinedBytes)} · {Format.Count(QuarantinedCount)} öğe, geri alınabilir";
    public string PurgedText => $"Kalıcı silindi {Format.Bytes(PurgedBytes)} · geri alınamaz";
}

public sealed class CleanSession(IAppBackend backend, Func<DateTimeOffset>? clock = null)
{
    sealed class Open(string id, string title, object? owner, DateTimeOffset startedAt, Dictionary<string, long> before, long ledgerBefore)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public object? Owner { get; } = owner;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public Dictionary<string, long> Before { get; } = before;
        public long LedgerBefore { get; } = ledgerBefore;
        public HashSet<string> Roots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Unit> Units { get; } = [];
        public long QuarantinedBytes { get; set; }
        public int QuarantinedCount { get; set; }
        public int PurgedCount { get; set; }
        public bool DryRun { get; set; }
    }

    sealed class Closer(CleanSession owner, string? id) : IDisposable
    {
        public void Dispose()
        {
            if (id is not null)
                owner.End(id);
        }
    }

    readonly Lock _lock = new();
    Open? _open;

    public string? CurrentId => _open?.Id;
    public object? Owner => _open?.Owner;
    public bool IsOpen => _open is not null;
    public SessionReport? Last { get; private set; }

    public event Action<SessionReport>? Finished;

    DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.Now;

    static string Key(string root) => root.TrimEnd('\\', '/').ToUpperInvariant();

    Dictionary<string, long> Free()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in backend.Drives())
            map[Key(drive.Root)] = drive.FreeBytes;
        return map;
    }

    public string Begin(string title, object? owner = null)
    {
        End();
        var open = new Open(Guid.NewGuid().ToString("N"), title, owner, Now(), Free(), backend.ReadLedger().FreedBytes);
        lock (_lock)
            _open = open;
        return open.Id;
    }

    public IDisposable Scope(string title) => _open is null ? new Closer(this, Begin(title)) : new Closer(this, null);

    void Touch(Open open, Unit unit)
    {
        foreach (var path in unit.Paths)
        {
            string? root;
            try
            {
                root = Path.GetPathRoot(path);
            }
            catch (ArgumentException)
            {
                root = null;
            }
            if (!string.IsNullOrEmpty(root))
                open.Roots.Add(Key(root));
        }
    }

    public void Quarantined(Unit unit, long bytes, int count)
    {
        lock (_lock)
        {
            if (_open is not { } open)
                return;
            Touch(open, unit);
            open.QuarantinedBytes += Math.Max(0, bytes);
            open.QuarantinedCount += Math.Max(0, count);
            open.Units.Add(unit);
        }
    }

    public void Purged(Unit unit)
    {
        lock (_lock)
        {
            if (_open is not { } open)
                return;
            Touch(open, unit);
            open.PurgedCount++;
        }
    }

    public void MarkDryRun()
    {
        lock (_lock)
            if (_open is { } open)
                open.DryRun = true;
    }

    public SessionReport? End(string? id = null)
    {
        Open open;
        lock (_lock)
        {
            if (_open is null || id is not null && _open.Id != id)
                return null;
            open = _open;
            _open = null;
        }
        var after = Free();
        var roots = open.Roots.Count > 0 && open.Roots.Any(open.Before.ContainsKey)
            ? open.Roots.Where(open.Before.ContainsKey).ToList()
            : [.. open.Before.Keys];
        var report = new SessionReport
        {
            Id = open.Id,
            Title = open.Title,
            StartedAt = open.StartedAt,
            EndedAt = Now(),
            FreeBefore = roots.Sum(r => open.Before[r]),
            FreeAfter = roots.Sum(r => after.TryGetValue(r, out var free) ? free : open.Before[r]),
            QuarantinedBytes = open.QuarantinedBytes,
            QuarantinedCount = open.QuarantinedCount,
            PurgedBytes = Math.Max(0, backend.ReadLedger().FreedBytes - open.LedgerBefore),
            PurgedCount = open.PurgedCount,
            DryRun = open.DryRun,
            Units = [.. open.Units],
        };
        if (!report.IsEmpty || report.DryRun)
        {
            Last = report;
            Finished?.Invoke(report);
        }
        return report;
    }
}
