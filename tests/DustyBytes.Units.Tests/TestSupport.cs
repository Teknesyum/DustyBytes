using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Signals;

namespace DustyBytes.Units.Tests;

static class Tree
{
    public static ScanNode Dir(string name, params ScanNode[] children)
    {
        var node = new ScanNode { Name = name, IsDirectory = true, Children = children.Length == 0 ? [] : children.ToList() };
        foreach (var c in children)
            c.Parent = node;
        Recalculate(node);
        return node;
    }

    public static ScanNode File(string name, long size, DateTimeOffset? lastWrite = null)
    {
        var lw = lastWrite ?? Ctx.Now.AddDays(-30);
        return new ScanNode
        {
            Name = name,
            IsDirectory = false,
            Size = size,
            LogicalSize = size,
            LastWriteTicks = lw.UtcTicks,
            NewestWriteTicks = lw.UtcTicks,
        };
    }

    static void Recalculate(ScanNode node)
    {
        if (node.Children is not { Count: > 0 })
            return;
        node.Size = node.Children.Sum(c => c.Size);
        node.NewestWriteTicks = node.Children.Max(c => c.NewestWriteTicks);
    }
}

sealed class FakeUsageIndex : IUsageIndex
{
    public Dictionary<string, UsageSignal> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, UsageSignal> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, UsageSignal> Media { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GameInstall> GamesList { get; } = [];

    public UsageSignal ForExecutable(string exePath) => Executables.GetValueOrDefault(exePath, UsageSignal.Unknown);
    public UsageSignal ForFolder(string folder) => Folders.GetValueOrDefault(folder, UsageSignal.Unknown);
    public UsageSignal ForMedia(string filePath) => Media.GetValueOrDefault(filePath, UsageSignal.Unknown);
    public IReadOnlyList<GameInstall> Games => GamesList;
}

static class Ctx
{
    public static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    public static UnitContext Build(ScanNode root, IUsageIndex? usage = null, ProtectedList? protectedList = null, DateTimeOffset? now = null) =>
        new()
        {
            ScanResult = new ScanResult { Root = root },
            UsageIndex = usage ?? new FakeUsageIndex(),
            Protected = protectedList ?? new ProtectedList(new ProtectedRules()),
            Now = now ?? Now,
        };
}
