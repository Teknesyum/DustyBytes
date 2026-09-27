using System.ComponentModel;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class ViewModelTests
{
    static MainViewModel Shell(FakeBackend backend, bool withSnapshot = true, bool? accept = true)
    {
        var vm = new MainViewModel(backend);
        if (withSnapshot)
            vm.Session.SetSnapshot(FakeBackend.Snapshot());
        if (accept is { } answer)
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Confirm) && vm.Confirm is { } dialog)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (answer)
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

    [AvaloniaFact]
    public void Navigation_Starts_On_Overview_And_Switches_Screens()
    {
        var vm = Shell(new FakeBackend());
        Assert.Same(vm.Overview, vm.Navigation.Current);
        Assert.Equal(6, vm.NavItems.Count);
        vm.GoTo(vm.Quarantine);
        Assert.Same(vm.Quarantine, vm.Navigation.Current);
        Assert.Same(vm.Quarantine, vm.SelectedNav!.Screen);
        Assert.True(vm.Quarantine.IsActive);
        Assert.False(vm.Overview.IsActive);
    }

    [AvaloniaFact]
    public void NavigationStack_Push_And_Pop_Restore_Previous()
    {
        var stack = new NavigationStack();
        var a = new MainViewModel(new FakeBackend()).Overview;
        var b = new MainViewModel(new FakeBackend()).Offers;
        stack.Navigate(a);
        stack.Push(b);
        Assert.True(stack.CanPop);
        Assert.True(stack.Pop());
        Assert.Same(a, stack.Current);
        Assert.False(stack.Pop());
    }

    [AvaloniaFact]
    public async Task Progress_Is_Monotonic_And_Keeps_Nine_Lines()
    {
        var progress = new TaskProgressViewModel();
        var seen = new List<double>();
        progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskProgressViewModel.Value))
                seen.Add(progress.Value);
        };
        var gate = new TaskCompletionSource();
        var run = progress.RunAsync("Deneme", async (p, ct) =>
        {
            await gate.Task;
            return true;
        });
        progress.Report(new TaskStep("Bir", 40, null));
        progress.Report(new TaskStep("Bir", 20, null));
        progress.Report(new TaskStep("İki", 100, null));
        for (var i = 0; i < 20; i++)
        {
            progress.Tick();
            progress.AddLine("satır " + i, force: true);
        }
        Assert.True(progress.Value <= TaskProgressViewModel.Ceiling);
        Assert.Equal(TaskProgressViewModel.MaxLines, progress.Lines.Count);
        Assert.Equal("satır 19", progress.Lines[^1].Text);
        Assert.True(progress.Lines[^1].IsNewest);
        Assert.False(progress.Lines[0].IsNewest);
        gate.SetResult();
        await run;
        Assert.False(progress.IsRunning);
        for (var i = 1; i < seen.Count; i++)
            Assert.True(seen[i] >= seen[i - 1]);
    }

    [AvaloniaFact]
    public void Toasts_Are_Capped_Errors_Stay_And_Pause_Holds()
    {
        var vm = Shell(new FakeBackend());
        var error = vm.Fail("Hata");
        for (var i = 0; i < 5; i++)
            vm.Notify("Bilgi " + i);
        Assert.Equal(MainViewModel.toastMax, vm.Toasts.Count);
        Assert.Contains(error, vm.Toasts);

        vm.Tick(TimeSpan.FromSeconds(60));
        Assert.Single(vm.Toasts);
        Assert.Same(error, vm.Toasts[0]);

        var paused = vm.Notify("Bekle");
        paused.IsPaused = true;
        vm.Tick(TimeSpan.FromSeconds(60));
        Assert.Contains(paused, vm.Toasts);
        paused.IsPaused = false;
        vm.Tick(TimeSpan.FromSeconds(60));
        Assert.DoesNotContain(paused, vm.Toasts);
    }

    [AvaloniaFact]
    public async Task Offers_Filter_And_Selection_Exclude_External_Units()
    {
        var vm = Shell(new FakeBackend());
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        var offers = vm.Offers;
        Assert.Equal(4, offers.Cards.Count);
        Assert.True(offers.HasSmall);
        offers.ShowSmall = true;
        Assert.Equal(5, offers.Cards.Count);

        offers.SelectedFilter = offers.Filters.First(f => f.Label == "Oyun");
        Assert.All(offers.Cards, c => Assert.Equal(UnitKind.Game, c.Unit.Kind));

        foreach (var card in offers.Cards)
            card.IsSelected = true;
        Assert.Single(offers.Chosen);
        Assert.Equal("u1", offers.Chosen[0].Unit.Id);
        Assert.Equal(40_000_000_000, offers.SelectedBytes);
    }

    [AvaloniaFact]
    public async Task Offers_Quarantine_Then_Undo_Restores_Returned_Ids()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.Cards.First(c => c.Unit.Id == "u1").IsSelected = true;

        await vm.Offers.QuarantineSelectedCommand.ExecuteAsync(null);
        await Settle();

        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("u1", request.UnitId);
        Assert.DoesNotContain(vm.Session.Snapshot!.Units, u => u.Id == "u1");
        var toast = Assert.Single(vm.Toasts, t => t.HasAction);
        Assert.Equal("Geri al", toast.ActionText);

        await toast.ActCommand.ExecuteAsync(null);
        await Settle();
        var restore = Assert.Single(backend.Requests, r => r.Op == Ops.Restore);
        Assert.Equal(["u1-0"], restore.Items);
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == "u1");
    }

    [AvaloniaFact]
    public async Task Offers_Dry_Run_Keeps_Cards()
    {
        var backend = new FakeBackend { DryRun = true };
        var vm = Shell(backend);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        vm.Offers.Cards.First(c => c.Unit.Id == "u1").IsSelected = true;
        await vm.Offers.QuarantineSelectedCommand.ExecuteAsync(null);
        await Settle();
        Assert.Contains(vm.Session.Snapshot!.Units, u => u.Id == "u1");
        Assert.Contains(vm.Toasts, t => t.Message.StartsWith("Prova", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Offers_One_Click_Quarantines_User_Data_Without_Confirm()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend, accept: null);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        await vm.Offers.RemoveOneCommand.ExecuteAsync(vm.Offers.Cards.First(c => c.Unit.Id == "u3"));
        await Settle();
        Assert.Null(vm.Confirm);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("u3", request.UnitId);
        Assert.True(request.IncludeUserData);
        var toast = Assert.Single(vm.Toasts, t => t.HasAction);
        Assert.Contains("7 gün", toast.Message);
    }

    [AvaloniaFact]
    public async Task Offers_One_Click_Direct_Delete_Needs_No_Confirm()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend, accept: null);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        var card = vm.Offers.Cards.First(c => c.Unit.Id == "u4");
        Assert.Equal("Temizle", card.ActionText);
        await vm.Offers.RemoveOneCommand.ExecuteAsync(card);
        await Settle();
        Assert.Null(vm.Confirm);
        Assert.Single(backend.Requests, r => r.Op == Ops.Delete);
    }

    [AvaloniaFact]
    public async Task Offers_Purge_Deletes_Without_Quarantine_After_Confirm()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend, accept: true);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        var card = vm.Offers.Cards.First(c => c.Unit.Id == "u3");
        Assert.True(card.CanPurge);
        await vm.Offers.PurgeOneCommand.ExecuteAsync(card);
        await Settle();
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Delete);
        Assert.Equal("u3", request.UnitId);
        Assert.Contains(vm.Toasts, t => t.Message.Contains("kalıcı silindi") && !t.HasAction);
    }

    [AvaloniaFact]
    public async Task Offers_Purge_Declined_Sends_Nothing()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend, accept: false);
        vm.GoTo(vm.Offers);
        await vm.Offers.Ready;
        await vm.Offers.PurgeOneCommand.ExecuteAsync(vm.Offers.Cards.First(c => c.Unit.Id == "u3"));
        await Settle();
        Assert.DoesNotContain(backend.Requests, r => r.Op is Ops.Delete or Ops.Quarantine);
    }

    [AvaloniaFact]
    public void IdsOf_Parses_Pipe_Suffix()
    {
        var response = new WorkerResponse
        {
            Id = "x",
            Items = [new ItemResult(@"C:\a|id1", true, ""), new ItemResult(@"C:\b|id2", false, ""), new ItemResult(@"C:\c", true, "")],
        };
        Assert.Equal(["id1"], OffersViewModel.IdsOf(response));
    }

    [AvaloniaFact]
    public async Task Overview_Pending_Reads_Same_Quarantine_As_Quarantine_Screen()
    {
        var vm = Shell(FakeBackend.Rich());
        vm.GoTo(vm.Overview);
        await Settle();
        Assert.Equal("5,59 GB, 2 öğe", vm.Overview.PendingText);
        vm.GoTo(vm.Quarantine);
        await Settle();
        Assert.Equal(vm.Overview.PendingText, vm.Quarantine.PendingText);
    }

    [AvaloniaFact]
    public void Map_Folder_Takes_Kind_Of_Units_Inside()
    {
        var vm = Shell(new FakeBackend());
        var kinds = vm.Map.Entries.ToDictionary(e => e.Name, e => e.Kind);
        Assert.Equal(UnitKind.Game, kinds["Oyunlar"]);
        Assert.Equal(UnitKind.Film, kinds["Videolar"]);
        Assert.Equal(UnitKind.DevArtifact, kinds["Kod"]);
        Assert.Null(kinds["Windows"]);
    }

    [Fact]
    public void Counts_Use_Turkish_Thousands_Separator() => Assert.Equal("6.254", Format.Count(6254));

    [AvaloniaFact]
    public async Task Quarantine_Purge_Needs_No_Confirm()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend, accept: null);
        await vm.Session.RefreshQuarantineAsync(vm);
        vm.GoTo(vm.Quarantine);
        await Settle();
        Assert.Equal(2, vm.Quarantine.Items.Count);
        Assert.True(vm.Quarantine.HasWarnings);
        var id = vm.Quarantine.Items[0].Id;
        vm.Quarantine.Items[0].IsSelected = true;
        await vm.Quarantine.PurgeCommand.ExecuteAsync(null);
        await Settle();
        Assert.Null(vm.Confirm);
        var purge = Assert.Single(backend.Requests, r => r.Op == Ops.Purge);
        Assert.Equal([id], purge.Items);
    }

    [AvaloniaFact]
    public async Task Quarantine_Empty_All_Sends_Target_All_Without_Confirm()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend, accept: null);
        await vm.Session.RefreshQuarantineAsync(vm);
        vm.GoTo(vm.Quarantine);
        await Settle();
        await vm.Quarantine.EmptyAllCommand.ExecuteAsync(null);
        await Settle();
        Assert.Null(vm.Confirm);
        var purge = Assert.Single(backend.Requests, r => r.Op == Ops.Purge);
        Assert.Equal(Targets.All, purge.Target);
        Assert.Empty(purge.Items);
    }

    [AvaloniaFact]
    public async Task Quarantine_Restore_Needs_No_Confirm()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend, accept: null);
        await vm.Session.RefreshQuarantineAsync(vm);
        vm.GoTo(vm.Quarantine);
        await Settle();
        vm.Quarantine.Items.First(i => i.Id == "q2").IsSelected = true;
        await vm.Quarantine.RestoreCommand.ExecuteAsync(null);
        await Settle();
        Assert.Null(vm.Confirm);
        var restore = Assert.Single(backend.Requests, r => r.Op == Ops.Restore);
        Assert.Equal(["q2"], restore.Items);
    }

    [AvaloniaFact]
    public async Task Cleanup_Never_Sends_Empty_System_Clean()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend);
        vm.GoTo(vm.Cleanup);
        await Settle();
        foreach (var task in vm.Cleanup.SystemTasks)
            task.IsChecked = false;
        Assert.True(vm.Cleanup.CleanCommand.CanExecute(null));
        await vm.Cleanup.CleanCommand.ExecuteAsync(null);
        await Settle();
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.SystemClean);
        var clean = Assert.Single(backend.Requests, r => r.Op == Ops.Clean);
        Assert.Equal(["chrome/cache"], clean.Items);
        Assert.All(backend.Requests, r => Assert.NotEmpty(r.Items));
    }

    [AvaloniaFact]
    public async Task Cleanup_Warning_Options_And_Running_Rules_Start_Unchecked()
    {
        var vm = Shell(FakeBackend.Rich());
        vm.GoTo(vm.Cleanup);
        await Settle();
        var chrome = vm.Cleanup.Rules.First(r => r.Info.Rule.Id == "chrome");
        Assert.True(chrome.Options.First(o => o.Id == "cache").IsChecked);
        Assert.False(chrome.Options.First(o => o.Id == "cookies").IsChecked);
        Assert.All(vm.Cleanup.Rules.First(r => r.Running).Options, o => Assert.False(o.IsChecked));
        Assert.False(vm.Cleanup.SystemTasks.First(t => !t.Available).IsChecked);
    }

    static async Task<UninstallViewModel> OpenUninstall(MainViewModel vm)
    {
        vm.GoTo(vm.Programs);
        await Settle();
        vm.Programs.Selected = vm.Programs.Rows.First(r => r.Name == "Eski Editör");
        vm.Programs.UninstallCommand.Execute(null);
        await Settle();
        return Assert.IsType<UninstallViewModel>(vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Uninstall_Hides_Low_Tier_And_Checks_High_Only()
    {
        var vm = Shell(FakeBackend.Rich());
        var un = await OpenUninstall(vm);
        Assert.Equal(2, un.Leftovers.Count);
        Assert.True(un.HasMore);
        Assert.True(un.Leftovers.First(l => l.Tier == ConfidenceTier.High).IsChecked);
        Assert.False(un.Leftovers.First(l => l.Tier == ConfidenceTier.Medium).IsChecked);
        un.ShowMoreCommand.Execute(null);
        Assert.Equal(3, un.Leftovers.Count);
    }

    [AvaloniaFact]
    public async Task Uninstall_Restore_Point_Failure_Asks_Then_Continues_With_Flag()
    {
        var backend = FakeBackend.Rich();
        var after = JsonSerializer.Serialize(FakeBackend.Leftovers(true), UninstallJson.Default.LeftoverSnapshot);
        backend.Respond = r => r.Op != Ops.Uninstall
            ? new WorkerResponse { Id = r.Id, Ok = true }
            : r.Items.Contains(UninstallHandlers.ContinueWithoutRestorePoint)
                ? new WorkerResponse { Id = r.Id, Ok = true, Message = "Kaldırıldı", Payload = after, Items = [new ItemResult("vendor", true, "")] }
                : new WorkerResponse { Id = r.Id, Ok = false, Items = [new ItemResult("restore-point", false, "Kapalı")] };
        var vm = Shell(backend);
        var un = await OpenUninstall(vm);

        await un.UninstallCommand.ExecuteAsync(null);
        await Settle();

        var sent = backend.Requests.Where(r => r.Op == Ops.Uninstall).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Empty(sent[0].Items);
        Assert.Contains(UninstallHandlers.ContinueWithoutRestorePoint, sent[1].Items);
        Assert.True(un.Uninstalled);
        Assert.True(un.RemoveLeftoversCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Uninstall_Dry_Run_Disables_Leftover_Removal()
    {
        var backend = FakeBackend.Rich();
        backend.DryRun = true;
        var after = JsonSerializer.Serialize(FakeBackend.Leftovers(true), UninstallJson.Default.LeftoverSnapshot);
        backend.Respond = r => new WorkerResponse { Id = r.Id, Ok = true, DryRun = true, Payload = after };
        var vm = Shell(backend);
        var un = await OpenUninstall(vm);
        await un.UninstallCommand.ExecuteAsync(null);
        await Settle();
        Assert.True(un.Uninstalled);
        Assert.False(un.RemoveLeftoversCommand.CanExecute(null));
        Assert.Contains("Prova", un.RemoveTip);
    }

    [AvaloniaFact]
    public async Task Overview_Shows_Cache_Then_Refreshes()
    {
        var backend = FakeBackend.Rich();
        var vm = Shell(backend, withSnapshot: false);
        await vm.StartAsync();
        await Settle();
        Assert.True(vm.Overview.HasSnapshot);
        Assert.False(vm.Overview.IsRefreshing);
        Assert.False(vm.Overview.HasError);
        Assert.Equal("Yenile", vm.Overview.PrimaryText);
        Assert.False(vm.Overview.FastScanEnabled);
        Assert.False(string.IsNullOrWhiteSpace(vm.Overview.FastScanTip));
    }

    [AvaloniaFact]
    public async Task Start_Refreshes_From_Cache_And_Falls_Back_To_Full_Scan()
    {
        var backend = FakeBackend.Rich();
        backend.Cached = FakeBackend.Snapshot();
        var vm = Shell(backend, withSnapshot: false);
        await vm.StartAsync();
        await Settle();
        Assert.Equal(["refresh", "full"], backend.ScanCalls);

        backend.ScanCalls.Clear();
        backend.Refreshed = FakeBackend.Snapshot();
        await vm.Overview.StartScanCommand.ExecuteAsync(null);
        Assert.Equal(["refresh"], backend.ScanCalls);
        Assert.Same(backend.Refreshed, vm.Session.Snapshot);

        backend.ScanCalls.Clear();
        await vm.Overview.RescanCommand.ExecuteAsync(null);
        Assert.Equal(["full"], backend.ScanCalls);
    }

    [AvaloniaFact]
    public async Task Start_Without_Cache_Scans_In_Full()
    {
        var backend = new FakeBackend();
        var vm = Shell(backend, withSnapshot: false);
        await vm.StartAsync();
        await Settle();
        Assert.Equal(["full"], backend.ScanCalls);
        Assert.Equal("Yenile", vm.Overview.PrimaryText);
    }

    [AvaloniaFact]
    public async Task Overview_Scan_Error_Shows_Retry()
    {
        var backend = new FakeBackend { ScanError = new IOException("Disk okunamadı") };
        var vm = Shell(backend, withSnapshot: false);
        await vm.StartAsync();
        await Settle();
        Assert.True(vm.Overview.HasError);
        Assert.Equal("Yeniden dene", vm.Overview.PrimaryText);
        Assert.Contains(vm.Toasts, t => t.IsError);
    }

    [Fact]
    public void Window_Placement_Off_Screen_Is_Not_Visible()
    {
        var screens = new List<(double, double, double, double)> { (0, 0, 1920, 1040) };
        Assert.True(WindowStateStore.IsVisible(new WindowPlacement(100, 100, 1200, 780, false), screens));
        Assert.False(WindowStateStore.IsVisible(new WindowPlacement(3000, 100, 1200, 780, false), screens));
        Assert.False(WindowStateStore.IsVisible(new WindowPlacement(100, -500, 1200, 400, false), screens));
    }

    [Fact]
    public void Window_Placement_Round_Trips()
    {
        var path = Path.Combine(Path.GetTempPath(), "dustybytes-test-" + Guid.NewGuid().ToString("N"), "window.json");
        var store = new WindowStateStore(path);
        store.Save(new WindowPlacement(10, 20, 1000, 700, true));
        Assert.Equal(new WindowPlacement(10, 20, 1000, 700, true), store.Load());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [AvaloniaFact]
    public void ProgramsShowLoadingWhileListIsRead()
    {
        var backend = new FakeBackend { ProgramsGate = new TaskCompletionSource() };
        var vm = Shell(backend);
        var changed = new List<string?>();
        vm.Programs.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.GoTo(vm.Programs);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Programs.IsLoading);
        Assert.Contains(nameof(ProgramsViewModel.IsLoading), changed);
        Assert.False(vm.Programs.IsEmpty);
        backend.ProgramsGate.SetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Programs.IsLoading);
        Assert.True(vm.Programs.HasRows || vm.Programs.IsEmpty);
    }
}
