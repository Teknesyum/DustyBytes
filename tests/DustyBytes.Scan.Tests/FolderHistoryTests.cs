namespace DustyBytes.Scan.Tests;

public sealed class FolderHistoryTests : IDisposable
{
    const long Mb = 1L << 20;
    const long Gb = 1L << 30;
    static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    readonly string _db = Path.Combine(Path.GetTempPath(), "dustybytes-history-" + Guid.NewGuid().ToString("N")[..8] + ".db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_db) + "*"))
            File.Delete(f);
    }

    static ScanResult Tree(params (string Path, long Size)[] dirs)
    {
        var root = new ScanNode { Name = @"C:\", IsDirectory = true, Children = [] };
        var made = new Dictionary<string, ScanNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };
        foreach (var (path, size) in dirs)
        {
            var parent = root;
            var walked = "";
            foreach (var part in path.Split('\\'))
            {
                walked = walked.Length == 0 ? part : walked + "\\" + part;
                if (!made.TryGetValue(walked, out var node))
                {
                    node = new ScanNode { Name = part, IsDirectory = true, Parent = parent, Children = [] };
                    parent.Children!.Add(node);
                    made[walked] = node;
                }
                parent = node;
            }
            parent.Size = size;
        }
        root.Size = root.Children!.Sum(c => c.Size);
        return new ScanResult { Root = root, FinishedAt = Start };
    }

    static FolderBaseline Before(params (string Path, long Size)[] folders) =>
        new(Start.AddDays(-1), [.. folders.Select(f => new FolderSize(f.Path, f.Size))]);

    static IReadOnlyList<FolderSize> Now(params (string Path, long Size)[] folders) =>
        [.. folders.Select(f => new FolderSize(f.Path, f.Size))];

    [Fact]
    public void Extract_Keeps_Big_Folders_Up_To_Depth_Three()
    {
        var result = Tree(
            (@"Users", 5 * Gb),
            (@"Users\ben", 4 * Gb),
            (@"Users\ben\Downloads", 3 * Gb),
            (@"Users\ben\Downloads\eski", 2 * Gb),
            (@"Users\ben\Belgeler", 50 * Mb),
            (@"Windows", 99 * Mb));
        var paths = FolderHistory.Extract(result).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { @"C:\Users", @"C:\Users\ben", @"C:\Users\ben\Downloads" }.ToHashSet(), paths);
    }

    [Fact]
    public void Extract_Skips_Reparse_Points_And_Caps_Rows()
    {
        var result = Tree((@"Bag", 10 * Gb));
        result.Root.Child("Bag")!.Flags = NodeFlags.ReparsePoint;
        Assert.Empty(FolderHistory.Extract(result));

        var many = Tree([.. Enumerable.Range(0, FolderHistory.MaxRowsPerSnapshot + 200).Select(i => ("K" + i, (200L + i) * Mb))]);
        var rows = FolderHistory.Extract(many);
        Assert.Equal(FolderHistory.MaxRowsPerSnapshot, rows.Count);
        Assert.Equal(many.Root.Children!.Max(c => c.Size), rows.Max(r => r.Size));
    }

    [Fact]
    public void Save_Stores_One_Snapshot_Per_Day_And_Replaces_It()
    {
        var index = new ScanIndex(_db);
        index.Save(Tree((@"Oyunlar", 10 * Gb)), Start);
        index.Save(Tree((@"Oyunlar", 12 * Gb)), Start.AddHours(3));
        Assert.Equal(1, index.HistoryDays(@"C:\"));
        Assert.Null(index.Baseline(@"C:\", Start.AddHours(3)));
        var next = index.Baseline(@"C:\", Start.AddDays(1))!;
        Assert.Equal(12 * Gb, Assert.Single(next.Folders).Size);
        Assert.Single(index.List());
    }

    [Fact]
    public void Save_Skips_Cancelled_Scans()
    {
        var index = new ScanIndex(_db);
        var cancelled = Tree((@"Oyunlar", 10 * Gb));
        index.Save(new ScanResult { Root = cancelled.Root, Cancelled = true, FinishedAt = Start });
        Assert.Equal(0, index.HistoryDays(@"C:\"));
    }

    [Fact]
    public void Save_Keeps_Existing_Node_Cache_Working()
    {
        var index = new ScanIndex(_db);
        index.Save(Tree((@"Oyunlar", 10 * Gb), (@"Oyunlar\A", 4 * Gb)), Start);
        var back = index.Load(@"C:\")!;
        Assert.Equal(10 * Gb, back.Root.Child("Oyunlar")!.Size);
        Assert.Equal(4 * Gb, back.Root.Find(@"C:\Oyunlar\A")!.Size);
    }

    [Fact]
    public void Old_Snapshots_Are_Pruned_After_Thirty_Days()
    {
        var index = new ScanIndex(_db);
        index.Save(Tree((@"A", 1 * Gb)), Start);
        index.Save(Tree((@"A", 2 * Gb)), Start.AddDays(10));
        Assert.Equal(2, index.HistoryDays(@"C:\"));
        index.Save(Tree((@"A", 3 * Gb)), Start.AddDays(31));
        Assert.Equal(2, index.HistoryDays(@"C:\"));
        Assert.Equal(2 * Gb, index.Baseline(@"C:\", Start.AddDays(31))!.Folders.Single().Size);
        index.Save(Tree((@"A", 4 * Gb)), Start.AddDays(45));
        Assert.Equal(2, index.HistoryDays(@"C:\"));
    }

    [Fact]
    public void Store_Stays_Under_Five_Megabytes()
    {
        var index = new ScanIndex(_db);
        var name = new string('x', 200);
        var dirs = Enumerable.Range(0, FolderHistory.MaxRowsPerSnapshot).Select(i => (name + i, 200 * Mb + i)).ToArray();
        for (var day = 0; day < 30; day++)
            index.Save(Tree(dirs), Start.AddDays(day));
        Assert.True(index.HistoryBytes() <= FolderHistory.MaxStoreBytes);
        Assert.True(index.HistoryDays(@"C:\") >= 1);
        Assert.NotNull(index.Baseline(@"C:\", Start.AddDays(30)));
        Assert.True(new FileInfo(_db).Length < 5 * 1024 * 1024 + 20 * 1024 * 1024);
    }

    [Fact]
    public void Baseline_Is_Newest_Snapshot_At_Least_A_Day_Old()
    {
        var index = new ScanIndex(_db);
        index.Save(Tree((@"A", 1 * Gb)), Start);
        index.Save(Tree((@"A", 2 * Gb)), Start.AddDays(2));
        index.Save(Tree((@"A", 3 * Gb)), Start.AddDays(3));
        Assert.Equal(2 * Gb, index.Baseline(@"C:\", Start.AddDays(3).AddHours(1))!.Folders.Single().Size);
        Assert.Equal(1 * Gb, index.Baseline(@"C:\", Start.AddDays(2))!.Folders.Single().Size);
        Assert.Null(index.Baseline(@"C:\", Start));
        Assert.Null(index.Baseline(@"D:\", Start.AddDays(3)));
    }

    [Fact]
    public void Compare_Without_Baseline_Or_Growth_Is_Empty()
    {
        Assert.Null(FolderHistory.Compare(Now((@"C:\A", 5 * Gb)), null));
        Assert.Null(FolderHistory.Compare(Now((@"C:\A", 5 * Gb)), Before((@"C:\A", 5 * Gb))));
        Assert.Null(FolderHistory.Compare(Now((@"C:\A", 5 * Gb)), Before((@"C:\A", 5 * Gb - 50 * Mb))));
        Assert.Null(FolderHistory.Compare(Now((@"C:\A", 2 * Gb)), Before((@"C:\A", 5 * Gb))));
    }

    [Fact]
    public void Compare_Picks_Deepest_Dominant_Folder_And_Does_Not_Repeat_Parents()
    {
        var before = Before((@"C:\Users", 20 * Gb), (@"C:\Users\ben", 19 * Gb), (@"C:\Users\ben\Downloads", 5 * Gb), (@"C:\Users\ben\Belgeler", 8 * Gb));
        var now = Now((@"C:\Users", 32 * Gb), (@"C:\Users\ben", 31 * Gb), (@"C:\Users\ben\Downloads", 17 * Gb), (@"C:\Users\ben\Belgeler", 8 * Gb));
        var growth = FolderHistory.Compare(now, before)!;
        Assert.Equal(12 * Gb, growth.TotalBytes);
        var item = Assert.Single(growth.Top);
        Assert.Equal(@"C:\Users\ben\Downloads", item.Path);
        Assert.Equal("Downloads", item.Name);
        Assert.Equal(12 * Gb, item.Bytes);
        Assert.Equal(before.At, growth.Since);
    }

    [Fact]
    public void Compare_Keeps_Parent_When_Growth_Is_Spread()
    {
        var before = Before((@"C:\Users", 20 * Gb), (@"C:\Users\ben", 10 * Gb), (@"C:\Users\ben\A", 4 * Gb), (@"C:\Users\ben\B", 4 * Gb));
        var now = Now((@"C:\Users", 30 * Gb), (@"C:\Users\ben", 20 * Gb), (@"C:\Users\ben\A", 9 * Gb), (@"C:\Users\ben\B", 9 * Gb));
        var growth = FolderHistory.Compare(now, before)!;
        Assert.Equal(10 * Gb, growth.TotalBytes);
        var item = Assert.Single(growth.Top);
        Assert.Equal(@"C:\Users\ben", item.Path);
    }

    [Fact]
    public void Compare_Returns_Top_Three_Of_Separate_Folders_And_New_Folders_Count()
    {
        var before = Before((@"C:\A", 10 * Gb), (@"C:\B", 10 * Gb), (@"C:\C", 10 * Gb), (@"C:\D", 10 * Gb));
        var now = Now((@"C:\A", 11 * Gb), (@"C:\B", 15 * Gb), (@"C:\C", 13 * Gb), (@"C:\D", 12 * Gb), (@"C:\Yeni", 6 * Gb));
        var growth = FolderHistory.Compare(now, before)!;
        Assert.Equal([@"C:\Yeni", @"C:\B", @"C:\C"], growth.Top.Select(t => t.Path));
        Assert.Equal(17 * Gb, growth.TotalBytes);
    }

    [Fact]
    public void Compare_Matches_Paths_Ignoring_Case()
    {
        var growth = FolderHistory.Compare(Now((@"C:\Users", 8 * Gb)), Before((@"c:\users", 5 * Gb)))!;
        Assert.Equal(3 * Gb, growth.TotalBytes);
    }

    [Fact]
    public void Merge_Combines_Drives()
    {
        var a = new FolderGrowth(5 * Gb, [new FolderGrowthItem(@"C:\A", 5 * Gb)], Start.AddDays(-2));
        var b = new FolderGrowth(7 * Gb, [new FolderGrowthItem(@"D:\B", 7 * Gb)], Start.AddDays(-1));
        var merged = FolderHistory.Merge([a, null, b])!;
        Assert.Equal(12 * Gb, merged.TotalBytes);
        Assert.Equal([@"D:\B", @"C:\A"], merged.Top.Select(t => t.Path));
        Assert.Equal(a.Since, merged.Since);
        Assert.Null(FolderHistory.Merge([null, null]));
    }

    [Fact]
    public void WeeklyGrowth_Compares_Newest_With_Snapshot_Closest_To_A_Week_Earlier()
    {
        var index = new ScanIndex(_db);
        Assert.Null(index.WeeklyGrowth(@"C:\"));
        index.Save(Tree((@"Downloads", 5 * Gb)), Start);
        Assert.Null(index.WeeklyGrowth(@"C:\"));
        index.Save(Tree((@"Downloads", 6 * Gb)), Start.AddDays(3));
        index.Save(Tree((@"Downloads", 9 * Gb)), Start.AddDays(7));
        index.Save(Tree((@"Downloads", 17 * Gb)), Start.AddDays(8));
        var growth = index.WeeklyGrowth(@"C:\")!;
        Assert.Equal(12 * Gb, growth.TotalBytes);
        Assert.Equal("Downloads", Assert.Single(growth.Top).Name);
        Assert.Equal(Start, growth.Since);
    }
}
