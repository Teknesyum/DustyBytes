using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class YerAcmaPlaniTests
{
    const long SafeTotal = 3_000_000_000 + 999_999 + 120_000_000;
    const long UnusedTotal = 40_000_000_000 + 8_000_000_000;
    const long UsedTotal = 20_000_000_000 + 900_000_000;

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<MainViewModel> Overview(ScanSnapshot? snapshot = null)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        var vm = new MainViewModel(backend);
        vm.Session.SetSnapshot(snapshot ?? FakeBackend.Snapshot());
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await vm.Overview.EstimateAsync();
        await Settle();
        return vm;
    }

    static async Task<MainViewModel> Cleanup()
    {
        var vm = new MainViewModel(FakeBackend.Rich());
        vm.GoTo(vm.Cleanup);
        await Settle();
        return vm;
    }

    [AvaloniaFact]
    public async Task Plan_Splits_The_Space_Into_Three_Steps_That_Add_Up()
    {
        var overview = (await Overview()).Overview;

        Assert.Equal(Format.Bytes(SafeTotal), overview.SafeSizeText);
        Assert.Equal(Format.Bytes(UnusedTotal), overview.UnusedSizeText);
        Assert.Equal(Format.Bytes(UsedTotal), overview.UsedSizeText);
        Assert.Equal(SafeTotal + UnusedTotal + UsedTotal, overview.MaxBytes);
        Assert.True(overview.HasPlanTotal);
        Assert.Equal($"Bunların hepsi silinirse en çok {Format.Bytes(SafeTotal + UnusedTotal + UsedTotal)} açılır", overview.PlanTotalText);
        Assert.True(overview.HasMore);
        Assert.False(overview.IsUnusedEmpty);
        Assert.True(overview.HasUsed);
        Assert.False(overview.IsUsedEmpty);
        Assert.Contains("90 gündür", overview.UnusedHow);
        Assert.Contains("programlar ekranından kaldır", OverviewViewModel.UsedHow);
    }

    [AvaloniaFact]
    public async Task Used_Step_Leads_To_Programs_Without_Touching_Files()
    {
        var vm = await Overview();
        vm.Overview.OpenProgramsCommand.Execute(null);
        await Settle();

        Assert.Same(vm.Programs, vm.SelectedNav!.Screen);
        Assert.Same(vm.Programs, vm.Navigation.Current);
    }

    [AvaloniaFact]
    public async Task Plan_Says_So_When_Nothing_Is_Long_Unused()
    {
        var snapshot = FakeBackend.Snapshot();
        var vm = await Overview(snapshot with { Units = [.. snapshot.Units.Where(u => u.Removal == RemovalMethod.DirectDelete)] });
        var overview = vm.Overview;

        Assert.False(overview.HasMore);
        Assert.True(overview.IsUnusedEmpty);
        Assert.False(overview.HasUsed);
        Assert.True(overview.IsUsedEmpty);
        Assert.Equal(Format.Bytes(0), overview.UnusedSizeText);
        Assert.Equal(overview.SafeBytes, overview.MaxBytes);
        Assert.False(overview.MoreSpaceCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cleanup_Comes_With_The_Recommended_Choice_And_Hides_The_Rest()
    {
        var cleanup = (await Cleanup()).Cleanup;

        Assert.StartsWith("Hangilerini seçmeliyim?", CleanupViewModel.GuideText);
        var chrome = Assert.Single(cleanup.RecommendedRules, g => g.Rule.Info.Rule.Id == "chrome");
        var cache = Assert.Single(chrome.Options);
        Assert.Equal("cache", cache.Id);
        Assert.True(cache.IsChecked);
        Assert.Equal(CleanAdvice.RecommendedText, cache.Advice.Text);
        Assert.Equal(CleanAdvice.RebuildsReason, cache.Advice.Reason);

        var others = Assert.Single(cleanup.OtherRules);
        var cookies = Assert.Single(others.Options);
        Assert.Equal("cookies", cookies.Id);
        Assert.False(cookies.IsChecked);
        Assert.Equal(CleanAdvice.CautionText, cookies.Advice.Text);
        Assert.Equal("Oturumlar kapanır", cookies.Advice.Reason);

        var edge = Assert.Single(cleanup.RecommendedRules, g => g.Rule.Running);
        Assert.False(Assert.Single(edge.Options).IsChecked);

        var temp = Assert.Single(cleanup.RecommendedTasks);
        Assert.Equal("temp", temp.Id);
        Assert.True(temp.IsChecked);
        var store = Assert.Single(cleanup.OtherTasks);
        Assert.Equal("component-store", store.Id);
        Assert.Equal(CleanAdvice.OptionalText, store.Advice.Text);
        Assert.Equal(CleanAdvice.TaskUnavailableReason, store.Advice.Reason);

        Assert.True(cleanup.HasOthers);
        Assert.False(cleanup.ShowOthers);
        Assert.False(cleanup.ShowOtherTasks);
        Assert.Equal("Diğer seçenekler (2)", cleanup.OthersText);
        cleanup.ToggleOthersCommand.Execute(null);
        Assert.True(cleanup.ShowOthers);
        Assert.True(cleanup.ShowOtherTasks);
        Assert.Equal("Diğer seçenekleri gizle", cleanup.OthersText);
    }

    [Fact]
    public void Advice_Comes_From_The_Rule_Data()
    {
        Assert.Equal(AdviceLevel.Recommended, CleanAdvice.Of(new CleanerOption { Id = "cache", Label = "Önbellek" }).Level);

        var shader = CleanAdvice.Of(new CleanerOption { Id = "shader", Label = "Gölgelendirici önbelleği", Warning = "Silinince oyunların ilk açılışı yavaşlar" });
        Assert.Equal(AdviceLevel.Optional, shader.Level);
        Assert.Equal("Silinince oyunların ilk açılışı yavaşlar", shader.Reason);

        var login = CleanAdvice.Of(new CleanerOption { Id = "login", Label = "Kayıtlı veriler", Sensitive = true });
        Assert.Equal(AdviceLevel.Caution, login.Level);
        Assert.Equal(CleanAdvice.SessionReason, login.Reason);

        var laptop = CleanAdvice.Of(new SystemTaskInfo("hiber", "Hazırda bekletme", 8_000_000_000, false, "hiberfil.sys", true, null, Warning: "Dizüstünde pil biterken iş kaybolabilir"));
        Assert.Equal(AdviceLevel.Caution, laptop.Level);
        Assert.Equal("Dizüstünde pil biterken iş kaybolabilir", laptop.Reason);

        var temp = CleanAdvice.Of(new SystemTaskInfo("temp", "Geçici", 1, true, "", true, null));
        Assert.Equal(AdviceLevel.Recommended, temp.Level);
        Assert.Equal(CleanAdvice.TaskReason, temp.Reason);
    }
}
