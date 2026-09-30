using DustyBytes.Core.Model;
using DustyBytes.Core.Protection;
using DustyBytes.Scan;
using DustyBytes.Scan.Duplicates;

namespace DustyBytes.Units.Tests;

public class DuplicateUnitsTests
{
    static readonly DateTimeOffset Old = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset New = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    static DuplicateCopy Copy(string path, DateTimeOffset created, long size = 100) => new(path, size, created, created);

    static DuplicateGroup Group(params DuplicateCopy[] copies) => new("ABCDEF0123456789ABCDEF0123456789", 100, copies);

    [Fact]
    public void Indirilenler_Disindaki_Kopya_Daha_Yeni_Olsa_Da_Kalir()
    {
        var keep = DuplicateUnits.PickKeep([
            Copy(@"C:\Users\ali\Downloads\film.mkv", Old),
            Copy(@"D:\Arşiv\film.mkv", New),
        ]);

        Assert.Equal(@"D:\Arşiv\film.mkv", keep);
    }

    [Fact]
    public void Ayni_Durumdakilerden_En_Eskisi_Kalir()
    {
        var keep = DuplicateUnits.PickKeep([
            Copy(@"D:\Yedek\film.mkv", New),
            Copy(@"E:\Filmler\film.mkv", Old),
            Copy(@"C:\Users\ali\AppData\Local\Temp\film.mkv", Old.AddYears(-1)),
        ]);

        Assert.Equal(@"E:\Filmler\film.mkv", keep);
    }

    [Fact]
    public void Hepsi_Gecici_Klasordeyse_En_Eskisi_Kalir()
    {
        var keep = DuplicateUnits.PickKeep([
            Copy(@"C:\Users\ali\Downloads\b.zip", New),
            Copy(@"C:\Temp\a.zip", Old),
        ]);

        Assert.Equal(@"C:\Temp\a.zip", keep);
        Assert.True(DuplicateUnits.IsTransient(@"C:\Users\ali\Downloads\b.zip"));
        Assert.False(DuplicateUnits.IsTransient(@"C:\Users\ali\Documents\DownloadsList\b.zip"));
    }

    [Fact]
    public void Birim_Yalniz_Fazlalari_Onerir_Ve_Kalacagi_Tasir()
    {
        var unit = DuplicateUnits.Build(Group(
            Copy(@"C:\Users\ali\Downloads\film.mkv", Old, 110),
            Copy(@"D:\Arşiv\film.mkv", New, 120),
            Copy(@"E:\Yedek\film.mkv", New.AddDays(1), 130)));

        Assert.Equal(UnitKind.Duplicate, unit.Kind);
        Assert.StartsWith("Duplicate-", unit.Id);
        Assert.Equal(RemovalMethod.Quarantine, unit.Removal);
        Assert.Equal(@"D:\Arşiv\film.mkv", unit.Keep);
        Assert.DoesNotContain(unit.Keep, unit.Paths);
        Assert.Equal(2, unit.Paths.Count);
        Assert.Equal(240, unit.SizeBytes);
        Assert.Equal("film.mkv", unit.Name);
        Assert.Contains("en eski", DuplicateUnits.Rule);
    }

    [Fact]
    public void Kalacak_Degisince_Eskisi_Oneriye_Doner()
    {
        var unit = DuplicateUnits.Build(Group(
            Copy(@"D:\Arşiv\film.mkv", Old),
            Copy(@"E:\Yedek\film.mkv", New)));

        var swapped = DuplicateUnits.WithKeep(unit, @"e:\yedek\FILM.mkv");

        Assert.Equal(@"E:\Yedek\film.mkv", swapped.Keep);
        Assert.Equal([@"D:\Arşiv\film.mkv"], swapped.Paths);
        Assert.Equal(unit.Id, swapped.Id);
        Assert.Same(unit, DuplicateUnits.WithKeep(unit, @"F:\yok.mkv"));
    }

    [Fact]
    public void Program_Ve_Korunan_Alanlar_Aday_Olmaz()
    {
        var root = new ScanNode { Name = @"C:\", IsDirectory = true, Children = [] };
        ScanNode Dir(ScanNode parent, string name)
        {
            var node = new ScanNode { Name = name, IsDirectory = true, Parent = parent, Children = [] };
            parent.Children!.Add(node);
            return node;
        }
        void File(ScanNode parent, string name) =>
            parent.Children!.Add(new ScanNode { Name = name, Parent = parent, LogicalSize = 50_000_000, Size = 50_000_000 });

        File(Dir(root, "Windows"), "big.bin");
        File(Dir(root, "Program Files"), "big.bin");
        File(Dir(Dir(Dir(root, "Users"), "ali"), "AppData"), "big.bin");
        var games = Dir(root, "Oyunlar");
        File(games, "big.bin");
        var docs = Dir(Dir(Dir(root, "Users"), "veli"), "Documents");
        File(docs, "big.bin");
        File(docs, "lib.dll");
        File(docs, "small.bin");
        ((List<ScanNode>)docs.Children!)[^1].LogicalSize = 1000;

        var owners = new[]
        {
            new Unit { Id = "Game-1", Kind = UnitKind.Game, Name = "Oyun", Paths = [@"C:\Oyunlar"], SizeBytes = 1 },
        };
        var list = DuplicateUnits.Candidates([root], DuplicateFinder.DefaultMinBytes, owners, ProtectedList.LoadDefault(), CancellationToken.None);

        Assert.Equal([@"C:\Users\veli\Documents\big.bin"], list.Select(c => c.Path));
    }
}
