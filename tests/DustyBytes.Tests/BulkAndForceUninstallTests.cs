using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class BulkAndForceUninstallTests
{
    static MainViewModel Shell(FakeBackend backend, bool accept = true)
    {
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(FakeBackend.Snapshot());
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Confirm) && vm.Confirm is { } dialog)
                Dispatcher.UIThread.Post(() =>
                {
                    if (accept)
                        dialog.AcceptCommand.Execute(null);
                    else
                        dialog.DeclineCommand.Execute(null);
                });
        };
        return vm;
    }

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Gone(string id, string name, params RemovalItem[] removed) =>
        JsonSerializer.Serialize(new LeftoverSnapshot
        {
            Id = "snap-" + id,
            Program = FakeBackend.Program(id, name, "Örnek", 0) with { InstallLocation = @"C:\Program Files\" + name },
            IsDiff = true,
            AutoRemoved = [.. removed],
        }, UninstallJson.Default.LeftoverSnapshot);

    static string Still(string id) =>
        JsonSerializer.Serialize(FakeBackend.Leftovers(true, stillInstalled: true) with { Id = "snap-" + id }, UninstallJson.Default.LeftoverSnapshot);

    static FakeBackend ThreePrograms()
    {
        var backend = FakeBackend.Rich();
        backend.Programs =
        [
            new ProgramInfo(FakeBackend.Program("p1", "Eski Editör", "Örnek", 900_000_000) with { InstallLocation = @"C:\Program Files\Eski Editör" }, UsageSignal.Unknown),
            new ProgramInfo(FakeBackend.Program("p2", "Oyun Başlatıcı", "Başka", 300_000_000), UsageSignal.Unknown),
            new ProgramInfo(FakeBackend.Program("p3", "Resim Aracı", "Üçüncü", 100_000_000), UsageSignal.Unknown),
        ];
        return backend;
    }

    static async Task<ProgramsViewModel> OpenPrograms(MainViewModel vm)
    {
        vm.GoTo(vm.Programs);
        await Settle();
        return vm.Programs;
    }

    static async Task<BulkUninstallViewModel> StartBulk(MainViewModel vm, params string[] names)
    {
        var programs = await OpenPrograms(vm);
        foreach (var row in programs.Rows.Where(r => names.Contains(r.Name)))
            row.IsChecked = true;
        await programs.BulkUninstallCommand.ExecuteAsync(null);
        await Settle();
        return Assert.IsType<BulkUninstallViewModel>(vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Checking_Rows_Swaps_Single_Button_For_Bulk()
    {
        var vm = Shell(ThreePrograms());
        var programs = await OpenPrograms(vm);
        Assert.True(programs.ShowSingle);
        Assert.False(programs.BulkUninstallCommand.CanExecute(null));
        programs.Rows[0].IsChecked = true;
        programs.Rows[1].IsChecked = true;
        Assert.False(programs.ShowSingle);
        Assert.True(programs.HasChecked);
        Assert.Equal(2, programs.CheckedCount);
        Assert.Contains("(2)", programs.BulkText);
        Assert.True(programs.BulkUninstallCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Bulk_Runs_Queue_In_Order_Restore_Point_Once_And_Summarizes()
    {
        var backend = ThreePrograms();
        backend.Respond = r => new WorkerResponse
        {
            Id = r.Id,
            Ok = true,
            Message = "Kaldırıldı",
            Payload = r.Target == "p1"
                ? Gone("p1", "Eski Editör",
                    new RemovalItem("a", LeftoverKind.Folder, @"C:\Program Files\Eski Editör", true, "Karantinaya taşındı", 40_000_000),
                    new RemovalItem("b", LeftoverKind.Folder, @"C:\ProgramData\Eski Editör", true, "Karantinaya taşındı", 10_000_000))
                : Gone(r.Target!, "Resim Aracı"),
            Items = r.Items.Contains(UninstallHandlers.SkipRestorePoint)
                ? [new ItemResult("vendor", true, ""), new ItemResult(UninstallHandlers.AutoClean, true, "Kesin kalıntı kalmadı")]
                : [new ItemResult("restore-point", true, ""), new ItemResult("vendor", true, ""), new ItemResult(UninstallHandlers.AutoClean, true, "1 kesin kalıntı temizlendi")],
        };
        var vm = Shell(backend);
        var bulk = await StartBulk(vm, "Eski Editör", "Resim Aracı");
        await bulk.Completion;
        await Settle();

        var sent = backend.Requests.Where(r => r.Op == Ops.Uninstall).ToList();
        Assert.Equal(["p1", "p3"], sent.Select(r => r.Target));
        Assert.All(sent, r => Assert.True(r.UserApproved));
        Assert.Equal([UninstallHandlers.AutoClean], sent[0].Items);
        Assert.Contains(UninstallHandlers.SkipRestorePoint, sent[1].Items);
        Assert.Contains(UninstallHandlers.AutoClean, sent[1].Items);
        Assert.All(bulk.Items, i => Assert.Equal(StepState.Done, i.State));
        Assert.True(bulk.Finished);
        Assert.Contains("2 program kaldırıldı", bulk.Summary);
        Assert.Equal(900_000_000 + 10_000_000 + 100_000_000, bulk.Items.Sum(i => i.Freed));
        Assert.Contains("Açılan yer", bulk.FreedText);
        Assert.DoesNotContain(vm.Programs.Rows, r => r.Name is "Eski Editör" or "Resim Aracı");
        Assert.True(bulk.BackCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Bulk_Cancel_Stops_After_Current_Item()
    {
        var backend = ThreePrograms();
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, Message = "Kaldırıldı", Payload = Gone(r.Target!, "x"), Items = [new ItemResult("vendor", true, "")] };
        var vm = Shell(backend);
        backend.BeforeRespond = (r, _) =>
        {
            if (vm.Navigation.Current is BulkUninstallViewModel b && b.CancelCommand.CanExecute(null))
                b.CancelCommand.Execute(null);
            return Task.CompletedTask;
        };
        var bulk = await StartBulk(vm, "Eski Editör", "Oyun Başlatıcı", "Resim Aracı");
        await bulk.Completion;
        await Settle();

        Assert.Single(backend.Requests, r => r.Op == Ops.Uninstall);
        Assert.Equal(StepState.Done, bulk.Items[0].State);
        Assert.All(bulk.Items.Skip(1), i => Assert.Equal(StepState.Skipped, i.State));
        Assert.Contains("2 program atlandı", bulk.Summary);
    }

    [AvaloniaFact]
    public async Task Bulk_Pauses_With_Wizard_Card_Until_Visible_Uninstaller_Exits()
    {
        var backend = ThreePrograms();
        var release = new TaskCompletionSource();
        backend.BeforeRespond = async (r, progress) =>
        {
            if (r.Target != "p1")
                return;
            progress.Report(new TaskStep(Uninstaller.VisibleStep, 0, null));
            await release.Task;
            progress.Report(new TaskStep(Uninstaller.VisibleStep, 100, null));
        };
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, Message = "Kaldırıldı", Payload = Gone(r.Target!, "x"), Items = [new ItemResult("vendor", true, "")] };
        var vm = Shell(backend);
        var bulk = await StartBulk(vm, "Eski Editör", "Oyun Başlatıcı");
        await Settle();

        Assert.True(bulk.WizardVisible);
        Assert.Contains("Eski Editör", bulk.WizardText);
        Assert.Single(backend.Requests, r => r.Op == Ops.Uninstall);
        Assert.Equal(StepState.Running, bulk.Items[0].State);

        release.SetResult();
        await bulk.Completion;
        await Settle();
        Assert.False(bulk.WizardVisible);
        Assert.Equal(2, backend.Requests.Count(r => r.Op == Ops.Uninstall));
    }

    [AvaloniaFact]
    public async Task Bulk_Failure_Is_Reported_And_Queue_Continues()
    {
        var backend = ThreePrograms();
        backend.Respond = r => r.Target == "p1"
            ? new WorkerResponse { Id = r.Id, Ok = false, Message = "Kaldırıcı hata kodu 1603 ile çıktı", Payload = Still("p1"), Items = [new ItemResult("vendor", false, "Kaldırıcı hata kodu 1603 ile çıktı")] }
            : new WorkerResponse { Id = r.Id, Ok = true, Message = "Kaldırıldı", Payload = Gone(r.Target!, "x"), Items = [new ItemResult("vendor", true, "")] };
        var vm = Shell(backend);
        var bulk = await StartBulk(vm, "Eski Editör", "Oyun Başlatıcı");
        await bulk.Completion;
        await Settle();

        Assert.Equal(StepState.Failed, bulk.Items[0].State);
        Assert.Contains("zorla", bulk.Items[0].Detail);
        Assert.Equal(StepState.Done, bulk.Items[1].State);
        Assert.Contains("1 program kaldırıldı, 1 program kaldırılamadı", bulk.Summary);
        Assert.Contains(vm.Programs.Rows, r => r.Name == "Eski Editör");
    }

    [AvaloniaFact]
    public async Task Bulk_Skips_Broken_Uninstaller_Without_Sending()
    {
        var backend = ThreePrograms();
        backend.Programs[0] = backend.Programs[0] with { Program = backend.Programs[0].Program with { UninstallerMissing = true } };
        var vm = Shell(backend);
        var programs = await OpenPrograms(vm);
        var broken = programs.Rows.First(r => r.Name == "Eski Editör");
        Assert.False(broken.CanCheck);
        var bulk = new BulkUninstallViewModel(vm, programs, [broken]);
        await bulk.RunAsync();
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Uninstall);
        Assert.Equal(StepState.Failed, bulk.Items[0].State);
    }

    static async Task<UninstallViewModel> Open(MainViewModel vm, string name)
    {
        var programs = await OpenPrograms(vm);
        programs.Selected = programs.Rows.First(r => r.Name == name);
        Assert.True(programs.UninstallCommand.CanExecute(null));
        programs.UninstallCommand.Execute(null);
        await Settle();
        return Assert.IsType<UninstallViewModel>(vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Missing_Uninstaller_Offers_Force_And_Sends_Force_Op()
    {
        var backend = ThreePrograms();
        backend.Programs[0] = backend.Programs[0] with { Program = backend.Programs[0].Program with { UninstallerMissing = true } };
        backend.Respond = r => new WorkerResponse
        {
            Id = r.Id,
            Ok = true,
            Message = "Eski Editör zorla kaldırıldı",
            Payload = Gone("p1", "Eski Editör", new RemovalItem("a", LeftoverKind.Folder, @"C:\Program Files\Eski Editör", true, "Karantinaya taşındı", 40_000_000)),
            Items =
            [
                new ItemResult("restore-point", true, ""),
                new ItemResult("snapshot", true, "3 iz bulundu"),
                new ItemResult(ForceUninstall.FilesStep, true, "1 kesin iz karantinaya alındı"),
                new ItemResult(ForceUninstall.EntryStep, true, "Program kaydı silindi"),
            ],
        };
        var vm = Shell(backend);
        var un = await Open(vm, "Eski Editör");

        Assert.True(un.ForceOffered);
        Assert.False(un.ShowUninstallRun);
        Assert.False(un.UninstallCommand.CanExecute(null));
        Assert.Contains("bulunamadı", un.ForceReason);
        un.KeepSettings = true;
        await un.ForceCommand.ExecuteAsync(null);
        await Settle();

        var sent = Assert.Single(backend.Requests, r => r.Op == Ops.ForceUninstall);
        Assert.Equal("p1", sent.Target);
        Assert.True(sent.UserApproved);
        Assert.Contains(UninstallHandlers.KeepSettings, sent.Items);
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Uninstall);
        Assert.False(un.ForceOffered);
        Assert.True(un.Uninstalled);
        Assert.Equal(StepState.Done, un.Steps.First(s => s.Key == ForceUninstall.EntryStep).State);
        Assert.Single(un.Cleaned);
        Assert.DoesNotContain(vm.Programs.Rows, r => r.Name == "Eski Editör");
    }

    [AvaloniaFact]
    public async Task Vendor_Failure_Offers_Force_Instead_Of_Leftover_Removal()
    {
        var backend = ThreePrograms();
        backend.Respond = r => new WorkerResponse
        {
            Id = r.Id,
            Ok = false,
            Message = "Kaldırıcı hata kodu 1603 ile çıktı",
            Payload = Still("p1"),
            Items = [new ItemResult("restore-point", true, ""), new ItemResult("vendor", false, "Kaldırıcı hata kodu 1603 ile çıktı")],
        };
        var vm = Shell(backend);
        var un = await Open(vm, "Eski Editör");
        Assert.False(un.ForceOffered);
        await un.UninstallCommand.ExecuteAsync(null);
        await Settle();

        Assert.True(un.VendorFailed);
        Assert.True(un.ForceOffered);
        Assert.False(un.ShowRemoveLeftovers);
        Assert.True(un.ForceCommand.CanExecute(null));
        Assert.Contains("bitiremedi", un.ForceReason);
    }

    [AvaloniaFact]
    public async Task Force_Is_Never_Offered_For_Drivers_Or_Updates()
    {
        var backend = FakeBackend.Rich();
        backend.Programs =
        [
            new ProgramInfo(FakeBackend.Program("d1", "Intel Graphics Driver", "Intel", 10) with { UninstallerMissing = true }, UsageSignal.Unknown),
            new ProgramInfo(FakeBackend.Program("k1", "Security Update for Windows (KB5034441)", "Microsoft Corporation", 10) with { UninstallerMissing = true }, UsageSignal.Unknown),
        ];
        var vm = Shell(backend);
        var programs = await OpenPrograms(vm);
        Assert.All(programs.Rows, r => Assert.False(r.CanForce));
        programs.Selected = programs.Rows[0];
        programs.UninstallCommand.Execute(null);
        await Settle();
        var un = Assert.IsType<UninstallViewModel>(vm.Navigation.Current);
        Assert.False(un.ForceOffered);
        Assert.False(un.ForceCommand.CanExecute(null));
    }
}
