using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class GercekSonucTests
{
    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Ready(Action<FakeBackend>? setup = null)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        setup?.Invoke(backend);
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await vm.Overview.EstimateAsync();
        await Settle();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Summary_Shows_Real_Drive_Difference_Even_When_Nothing_Changed()
    {
        var (vm, _) = await Ready();
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        var summary = vm.Tour.Summary!;
        Assert.Equal($"Gerçekte açılan: {Format.Bytes(0)}", summary.FreedText);
        Assert.True(summary.HasDriveText);
        Assert.StartsWith("C: önce ", summary.DriveText);
        Assert.Equal($"Tahmini: {Format.Bytes(vm.Tour.FreedBytes)}", summary.EstimateText);
    }

    [AvaloniaFact]
    public async Task Each_Step_Is_Written_To_The_Ledger_Before_The_Next_Starts()
    {
        long seenAtSystemClean = -1;
        var (vm, backend) = await Ready(b => b.BeforeRespond = (r, _) =>
        {
            if (r.Op == Ops.SystemClean)
                seenAtSystemClean = b.ReadLedger().FreedBytes;
            return Task.CompletedTask;
        });
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        Assert.Equal(FakeBackend.CleanFreed, seenAtSystemClean);
        Assert.Equal(FakeBackend.CleanFreed * 2 + FakeBackend.DeleteFreed, backend.ReadLedger().FreedBytes);
        Assert.Equal(3, backend.ReadLedger().Actions);
    }

    [AvaloniaFact]
    public async Task A_Tour_Stopped_Halfway_Keeps_What_Was_Freed()
    {
        var (vm, backend) = await Ready(b => b.BeforeRespond = (r, _) =>
            r.Op == Ops.SystemClean ? throw new OperationCanceledException() : Task.CompletedTask);
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        Assert.True(vm.Tour.IsDone);
        Assert.Contains(backend.ReadLedger().Entries, e => e.Bytes == FakeBackend.CleanFreed);
        Assert.Equal(FakeBackend.CleanFreed + FakeBackend.DeleteFreed, vm.Session.Ledger.FreedBytes);
    }

    [AvaloniaFact]
    public async Task Summary_Lists_What_Could_Not_Be_Deleted_With_Cause_And_Advice()
    {
        var (vm, backend) = await Ready(b => b.Respond = r => r.Op switch
        {
            Ops.Clean => new WorkerResponse
            {
                Id = r.Id,
                Ok = true,
                FreedBytes = 1_000,
                Items = [new ItemResult("chrome/cache", false, "Chrome çalışıyor", 0)],
                Tally = new PathTally { Shown = 3, Processed = 1, ProcessedBytes = 1_000, Locked = 2 },
            },
            Ops.Delete => new WorkerResponse
            {
                Id = r.Id,
                Ok = false,
                Message = "0/1 öğe işlendi",
                FreedBytes = 1_000_000_000,
                Items = [new ItemResult(@"C:\Kod\node_modules|u4-0", false, "Locked: Silinemedi — Tutan: Code (42), Code (43)", 1_000_000_000)],
            },
            _ => FakeBackend.Measured(r),
        });
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        var summary = vm.Tour.Summary!;
        Assert.True(summary.HasProblems);
        Assert.Equal(
        [
            "Chrome · Önbellek: Chrome çalışıyor. Programı kapatıp yeniden dene",
            "Önbellek ve geçici dosyalar: 2 dosya kullanımda. Açık programları (örneğin tarayıcıyı) kapatıp yeniden dene",
            "node_modules: Dosya kullanımda. Code programını kapatıp yeniden dene",
        ], summary.ProblemLines);
        Assert.Equal("Silinemeyenler (3)", summary.ProblemsTitle);

        var left = Assert.Single(vm.Session.Snapshot!.Units, u => u.Id == "u4");
        Assert.Equal(2_000_000_000, left.SizeBytes);
        Assert.Contains(@"C:\", backend.FreedRoots);
        Assert.Equal(1_000 + FakeBackend.CleanFreed + 1_000_000_000, backend.ReadLedger().FreedBytes);
    }

    [AvaloniaFact]
    public async Task A_Unit_That_Is_Already_Gone_Leaves_The_List()
    {
        var (vm, _) = await Ready(b => b.Respond = r => r.Op == Ops.Delete
            ? new WorkerResponse { Id = r.Id, Ok = false, Message = "0/1 öğe işlendi", Items = [new ItemResult(@"C:\Kod\node_modules|u4-0", false, "NotFound: Yol bulunamadı", 0)] }
            : FakeBackend.Measured(r));
        await vm.Overview.SafeCleanCommand.ExecuteAsync(null);
        await Settle();

        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == "u4");
        Assert.False(vm.Tour.Summary!.HasProblems);
    }

    [AvaloniaFact]
    public async Task Tour_End_Measures_Rules_Again_Even_When_Nothing_Was_Freed()
    {
        var (vm, backend) = await Ready();
        var before = backend.PreviewCalls.Count;
        await vm.StartTourAsync(TourMode.Ask);
        vm.Tour.EndCommand.Execute(null);
        await Settle();

        Assert.True(vm.Tour.IsDone);
        Assert.True(backend.PreviewCalls.Count > before);
    }

    [AvaloniaFact]
    public async Task Automatic_Quarantine_Purge_Lands_In_The_Ledger()
    {
        var (vm, backend) = await Ready();
        var at = DateTimeOffset.Now.AddHours(-2);
        backend.AutoPurges.Add(new AutoPurgeRecord(at, 5_000_000_000, 3, @"D:\"));
        await vm.Session.RefreshQuarantineAsync(vm);

        Assert.Equal(5_000_000_000, vm.Session.Ledger.FreedBytes);
        var entry = vm.Session.Ledger.Last!;
        Assert.Equal(@"D:\", entry.Root);
        Assert.Equal(at, entry.At);
        Assert.Empty(backend.AutoPurges);
    }

    [Fact]
    public void Auto_Purge_Log_Sums_Real_Purges_Per_Drive_And_Is_Taken_Once()
    {
        var at = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var records = AutoPurgeLog.Summarize(
        [
            new OpResult { Path = @"C:\Eski\a", Status = OpStatus.Done, Bytes = 100 },
            new OpResult { Path = @"C:\Eski\b", Status = OpStatus.Locked, Bytes = 50 },
            new OpResult { Path = @"C:\Eski\c", Status = OpStatus.DryRun, Bytes = 999 },
            new OpResult { Path = @"C:\Eski\d", Status = OpStatus.NotFound, Bytes = 0 },
            new OpResult { Path = @"D:\Film", Status = OpStatus.Done, Bytes = 70 },
        ], at);
        Assert.Equal([new AutoPurgeRecord(at, 150, 1, @"C:\"), new AutoPurgeRecord(at, 70, 1, @"D:\")], records.OrderBy(r => r.Root));

        var dir = Directory.CreateTempSubdirectory("db-autopurge-");
        try
        {
            var path = Path.Combine(dir.FullName, "auto-purge.log");
            AutoPurgeLog.Append(records, path);
            AutoPurgeLog.Append([], path);
            var taken = AutoPurgeLog.Take(path);
            Assert.Equal(records.Select(r => (r.Bytes, r.Count, r.Root, r.At.ToUnixTimeMilliseconds())),
                taken.Select(r => (r.Bytes, r.Count, r.Root, r.At.ToUnixTimeMilliseconds())));
            Assert.False(File.Exists(path));
            Assert.Empty(AutoPurgeLog.Take(path));
            Assert.Empty(Directory.GetFiles(dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Ledger_Absorbs_Purge_Records_With_Their_Own_Time()
    {
        var dir = Directory.CreateTempSubdirectory("db-ledger-");
        try
        {
            var ledger = new Ledger(Path.Combine(dir.FullName, "ledger.json"));
            ledger.Add(10);
            var first = DateTimeOffset.Now.AddDays(-1);
            var data = ledger.Absorb(
            [
                new AutoPurgeRecord(first.AddHours(1), 300, 2, @"D:\"),
                new AutoPurgeRecord(first, 200, 1, @"C:\"),
            ], root => new DriveSpace(root, 1_000, 500));

            Assert.Equal(510, data.FreedBytes);
            Assert.Equal(3, data.Actions);
            Assert.Equal([@"C:\", @"D:\"], data.Entries.Skip(1).Select(e => e.Root));
            Assert.Equal(first, data.Entries[1].At);
            Assert.Equal(510, new Ledger(ledger.Path).Read().FreedBytes);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Failure_Advice_Speaks_Plainly()
    {
        Assert.Equal("a.txt: Dosya kullanımda. chrome programını kapatıp yeniden dene",
            FailureAdvice.For("a.txt", "Locked: Kilitli — Tutan: chrome (1)")!.Text);
        Assert.Equal("a.txt: Dosya kullanımda. İlgili programı kapatıp yeniden dene",
            FailureAdvice.For("a.txt", "Locked: Kilitli")!.Text);
        Assert.Equal("Proje: Dosya kullanımda. Code, explorer programlarını kapatıp yeniden dene",
            FailureAdvice.For("Proje", "Failed: Yarım kaldı — Tutan: Code (4), explorer (5)")!.Text);
        Assert.Equal("Proje: Erişilemedi. Bilgisayarı yeniden başlatıp yeniden dene",
            FailureAdvice.For("Proje", "Failed: Erişim reddedildi")!.Text);
        Assert.StartsWith("Windows: Korumalı (Sistem klasörü)", FailureAdvice.For("Windows", "Denied: Sistem klasörü")!.Text);
        Assert.Null(FailureAdvice.For("Eski", "NotFound: Yol bulunamadı"));
        Assert.Equal("Disk Temizleme: cleanmgr çıktı kodu 2. Temizlik ekranından yeniden dene",
            FailureAdvice.For("Disk Temizleme", "cleanmgr çıktı kodu 2")!.Text);
        Assert.Empty(FailureAdvice.ForTally(new PathTally { Shown = 2, Processed = 2 }));
        Assert.Equal(2, FailureAdvice.ForTally(new PathTally { Shown = 4, Processed = 1, Locked = 2, Failed = 1 }).Count);
    }

    [Fact]
    public void Space_Meter_Reports_Only_Touched_Drives()
    {
        var backend = new FakeBackend
        {
            DriveList = [new DriveSpace(@"C:\", 500, 100), new DriveSpace(@"D:\", 900, 300)],
        };
        var meter = SpaceMeter.Start(backend);
        meter.Touch(@"D:\Oyunlar\Eski");
        backend.DriveList = [new DriveSpace(@"C:\", 500, 90), new DriveSpace(@"D:\", 900, 450)];

        var deltas = meter.Finish();
        var only = Assert.Single(deltas);
        Assert.Equal(150, only.Freed);
        Assert.Equal(150, SpaceMeter.Total(deltas));
        Assert.Equal($"D: önce {Format.Bytes(300)} boş, sonra {Format.Bytes(450)} boş", SpaceMeter.Describe(deltas));

        var untouched = SpaceMeter.Start(backend);
        backend.DriveList = [new DriveSpace(@"C:\", 500, 80), new DriveSpace(@"D:\", 900, 450)];
        Assert.Equal(0, SpaceMeter.Total(untouched.Finish()));
    }
}
