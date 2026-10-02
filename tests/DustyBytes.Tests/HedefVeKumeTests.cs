using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Model;

namespace DustyBytes.Tests;

public class HedefVeKumeTests
{
    const long Gib = 1L << 30;
    static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    static Unit U(string id, UnitKind kind, double gib, int? days, bool userData = false, double confidence = 1.0,
        RemovalMethod removal = RemovalMethod.Quarantine, DateTimeOffset? now = null) => new()
    {
        Id = id,
        Kind = kind,
        Name = id,
        Paths = [@"C:\Deneme\" + id],
        SizeBytes = (long)(gib * Gib),
        Usage = days is { } d ? new UsageSignal((now ?? Now).AddDays(-d), "prefetch", 0.9) : UsageSignal.Unknown,
        ContainsUserData = userData,
        Confidence = confidence,
        Removal = removal,
    };

    static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<(MainViewModel Vm, FakeBackend Backend)> Ready(IReadOnlyList<Unit>? units = null)
    {
        var backend = FakeBackend.Rich();
        backend.Respond = FakeBackend.Measured;
        var vm = new MainViewModel(backend);
        var snapshot = FakeBackend.Snapshot();
        vm.Session.SetSnapshot(units is null ? snapshot : snapshot with { Units = units });
        vm.GoTo(vm.Overview);
        await vm.Session.RefreshQuarantineAsync(vm);
        await vm.Overview.EstimateAsync();
        await Settle();
        return (vm, backend);
    }

    static IReadOnlyList<Unit> WithUnsure()
    {
        var now = DateTimeOffset.Now;
        return
        [
            U("g1", UnitKind.Game, 40, 500, now: now),
            U("k1", UnitKind.Folder, 30, 200, confidence: 0.4, now: now),
        ];
    }

    [Fact]
    public void Plan_Takes_Longest_Unused_First_And_Largest_Within_A_Band()
    {
        IReadOnlyList<Unit> units =
        [
            U("a", UnitKind.Game, 30, 600),
            U("b", UnitKind.Game, 50, 400),
            U("c", UnitKind.Film, 20, 250),
            U("d", UnitKind.Program, 10, 100),
        ];
        var plan = TargetPlanner.Build(100 * Gib, 5 * Gib, units, Now);
        Assert.Equal(["b", "a", "c"], plan.Units.Select(u => u.Id));
        Assert.True(plan.IsMet);
        Assert.Equal(0, plan.Shortfall);
        Assert.Equal("", plan.ShortText);
        Assert.StartsWith("100 GB için: güvenli küme ", plan.HeadText);
        Assert.Contains("aydır açılmamış 2 oyun", plan.HeadText);
    }

    [Fact]
    public void Plan_Picks_The_Smallest_Item_That_Covers_The_Rest()
    {
        IReadOnlyList<Unit> units =
        [
            U("x", UnitKind.Game, 50, 400),
            U("y", UnitKind.Game, 12, 400),
            U("z", UnitKind.Game, 8, 400),
        ];
        var plan = TargetPlanner.Build(10 * Gib, 0, units, Now);
        Assert.Equal(["y"], plan.Units.Select(u => u.Id));
        Assert.Equal(12 * Gib, plan.HeldBytes);
    }

    [Fact]
    public void Plan_Says_How_Much_Is_Missing()
    {
        var plan = TargetPlanner.Build(100 * Gib, 5 * Gib, [U("a", UnitKind.Game, 30, 600)], Now);
        Assert.False(plan.IsMet);
        Assert.Equal(65 * Gib, plan.Shortfall);
        Assert.Equal($"Hedefe {Format.Bytes(65 * Gib)} eksik kaldı; uzun süredir açılmamış başka öğe yok.", plan.ShortText);
    }

    [Fact]
    public void Plan_Leaves_User_Data_And_Uncertain_Units_Out()
    {
        IReadOnlyList<Unit> units =
        [
            U("p", UnitKind.Film, 50, 600, userData: true),
            U("q", UnitKind.Folder, 40, 600, confidence: 0.4),
            U("r", UnitKind.Game, 10, 600),
            U("dup", UnitKind.Duplicate, 30, 600),
            U("direct", UnitKind.DevArtifact, 30, 600, removal: RemovalMethod.DirectDelete),
            U("launch", UnitKind.Game, 30, 600, removal: RemovalMethod.Launcher),
            U("recent", UnitKind.Game, 30, 20),
        ];
        var plan = TargetPlanner.Build(200 * Gib, 0, units, Now);
        Assert.Equal(["r"], plan.Units.Select(u => u.Id));
        Assert.Equal(["p", "q"], plan.Skipped.Select(s => s.Unit.Id));
        Assert.Equal(TargetPlan.UserDataReason, plan.Skipped[0].Reason);
        Assert.Equal(TargetPlan.UncertainReason, plan.Skipped[1].Reason);
        Assert.Contains("sorulmadan plana girmez", plan.ShortText);
    }

    [Fact]
    public void Plan_Is_Honest_That_Quarantine_Frees_Nothing_Yet()
    {
        var plan = TargetPlanner.Build(60 * Gib, 14 * Gib, [U("a", UnitKind.Game, 49, 600)], Now);
        Assert.Equal($"Bu planla şimdi {Format.Bytes(14 * Gib)} boşalır, {Format.Bytes(49 * Gib)} karantinada 7 gün bekler; hemen gerekiyorsa 'Şimdi yer aç'.", plan.NowText);

        var safeOnly = TargetPlanner.Build(5 * Gib, 14 * Gib, [U("a", UnitKind.Game, 49, 600)], Now);
        Assert.Empty(safeOnly.Units);
        Assert.Equal($"Bu planla şimdi {Format.Bytes(14 * Gib)} boşalır.", safeOnly.NowText);

        var nothing = TargetPlanner.Build(5 * Gib, 0, [], Now);
        Assert.True(nothing.IsEmpty);
        Assert.Equal("5 GB için güvenle önerebileceğimiz bir şey yok.", nothing.HeadText);
    }

    [Fact]
    public void Plan_Skips_Excluded_Units()
    {
        IReadOnlyList<Unit> units = [U("a", UnitKind.Game, 30, 600), U("b", UnitKind.Game, 50, 400)];
        var plan = TargetPlanner.Build(40 * Gib, 0, units, Now, new HashSet<string> { "b" });
        Assert.Equal(["a"], plan.Units.Select(u => u.Id));
        Assert.DoesNotContain(plan.Skipped, s => s.Unit.Id == "b");
    }

    [Theory]
    [InlineData("60", 60.0)]
    [InlineData(" 1,5 GB ", 1.5)]
    [InlineData("20gb", 20.0)]
    public void Goal_Parses_Gigabytes(string text, double gib) =>
        Assert.Equal((long)Math.Round(gib * Gib), TargetViewModel.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Goal_Rejects_What_Is_Not_A_Size(string text) => Assert.Null(TargetViewModel.Parse(text));

    [Fact]
    public void Clusters_Group_By_Kind_And_Unused_Band()
    {
        IReadOnlyList<Unit> units =
        [
            U("g1", UnitKind.Game, 30, 400),
            U("g2", UnitKind.Game, 20, 500),
            U("g3", UnitKind.Game, 10, 200),
            U("f1", UnitKind.Film, 5, 400),
            U("s1", UnitKind.Series, 8, 400),
            U("g4", UnitKind.Game, 3, null),
            U("k1", UnitKind.Folder, 50, 400, confidence: 0.4),
        ];
        var (groups, unsure) = UnitClusters.Build(units, Now);
        Assert.Equal(4, groups.Count);
        Assert.Equal(["g1", "g2"], groups[0].Units.Select(u => u.Id));
        Assert.Equal("2 oyun, 12+ aydır açılmamış", groups[0].Title);
        Assert.Equal(["s1", "f1"], groups[1].Units.Select(u => u.Id));
        Assert.Equal("2 film, 12+ aydır açılmamış", groups[1].Title);
        Assert.Equal("1 oyun, 6–12 aydır açılmamış", groups[2].Title);
        Assert.Equal("1 oyun, ne zaman açıldığı bilinmiyor", groups[3].Title);
        Assert.Equal(["k1"], unsure.Select(u => u.Id));
        Assert.DoesNotContain(groups.SelectMany(g => g.Units), u => u.Id == "k1");
        Assert.Equal(5, UnitClusters.Decisions(units, Now));
    }

    [AvaloniaFact]
    public async Task Uncertain_Unit_Never_Joins_The_Bulk_Flow()
    {
        var (vm, backend) = await Ready(WithUnsure());
        await vm.StartTourAsync(TourMode.Ask);
        var tour = vm.Tour;
        Assert.Equal("Küme küme karar", tour.Title);
        Assert.Equal(2, tour.Steps.Count);
        var cluster = Assert.IsType<TourCluster>(tour.Steps[0]);
        Assert.Equal(["g1"], cluster.Items.Select(i => i.Card.Unit.Id));
        Assert.Equal("k1", Assert.IsType<UnitCard>(tour.Steps[1]).Unit.Id);

        await tour.QuarantineClusterCommand.ExecuteAsync(null);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("g1", request.UnitId);

        Assert.True(tour.IsCardStep);
        Assert.True(tour.IsUncertain);
        Assert.Equal("k1", tour.Current!.Unit.Id);
        Assert.Equal(ClusterChoice.Keep, tour.Recommended);
        Assert.True(tour.KeepIsRecommended);
        Assert.False(tour.AllIsRecommended);
        Assert.True(tour.Key(TourKey.Enter));
        Assert.True(tour.IsDone);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == "k1");
        Assert.Contains(tour.Summary!.Lines, l => l == "Yerinde kalan: 1 öğe");
    }

    [AvaloniaFact]
    public async Task Purge_On_An_Uncertain_Card_Needs_Two_Presses_And_Expires()
    {
        var (vm, backend) = await Ready(WithUnsure());
        await vm.StartTourAsync(TourMode.Ask);
        var tour = vm.Tour;
        tour.KeepCommand.Execute(null);
        Assert.Equal("k1", tour.Current!.Unit.Id);

        await tour.PurgeOneCommand.ExecuteAsync(null);
        Assert.True(tour.IsPurgeArmed);
        Assert.Equal(TwoStep.ArmedText, tour.PurgeText);
        vm.Tick(TimeSpan.FromSeconds(4.5));
        Assert.False(tour.IsPurgeArmed);

        await tour.PurgeOneCommand.ExecuteAsync(null);
        Assert.True(tour.Key(TourKey.Escape));
        Assert.False(tour.IsPurgeArmed);
        Assert.Equal("k1", tour.Current!.Unit.Id);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == "k1");

        await tour.PurgeOneCommand.ExecuteAsync(null);
        await tour.PurgeOneCommand.ExecuteAsync(null);
        var request = Assert.Single(backend.Requests, r => r.UnitId == "k1");
        Assert.Equal(Ops.Delete, request.Op);
        Assert.True(tour.IsDone);
    }

    [AvaloniaFact]
    public async Task Keys_Move_Keep_And_Follow_The_Recommendation()
    {
        var (vm, backend) = await Ready();
        await vm.StartTourAsync(TourMode.Ask);
        var tour = vm.Tour;
        Assert.Equal("u1", tour.Cluster!.Items[0].Card.Unit.Id);
        Assert.Equal("Enter: hepsini karantinaya al · Esc: kalsın · ← →: önceki ya da sonraki küme", tour.KeysText);

        Assert.True(tour.Key(TourKey.Right));
        Assert.Equal("u3", tour.Cluster!.Items[0].Card.Unit.Id);
        Assert.Equal("2 / 2", tour.StepText);
        Assert.StartsWith("Enter: seçerek", tour.KeysText);
        Assert.False(tour.Key(TourKey.Right));
        Assert.True(tour.Key(TourKey.Left));
        Assert.Equal("u1", tour.Cluster!.Items[0].Card.Unit.Id);

        Assert.True(tour.Key(TourKey.Escape));
        Assert.Equal("u3", tour.Cluster!.Items[0].Card.Unit.Id);
        Assert.False(tour.Key(TourKey.Left));
        Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);

        Assert.True(tour.Key(TourKey.Enter));
        Assert.True(tour.Cluster!.IsExpanded);
        Assert.Equal(ClusterChoice.All, tour.Recommended);
        Assert.True(tour.Key(TourKey.Enter));
        await Settle();
        Assert.True(tour.IsDone);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("u3", request.UnitId);
        Assert.False(tour.Key(TourKey.Enter));
    }

    [AvaloniaFact]
    public async Task Selecting_Drops_One_Item_With_One_Click()
    {
        var now = DateTimeOffset.Now;
        var (vm, backend) = await Ready(
        [
            U("g1", UnitKind.Game, 30, 400, now: now),
            U("g2", UnitKind.Game, 20, 500, now: now),
            U("g3", UnitKind.Game, 10, 600, now: now),
        ]);
        await vm.StartTourAsync(TourMode.Ask);
        var tour = vm.Tour;
        var cluster = Assert.Single(tour.Steps.Cast<TourCluster>());
        Assert.Equal("3 oyun, 12+ aydır açılmamış", cluster.Title);
        Assert.Equal("Hepsini karantinaya al", tour.AllText);

        tour.SelectCommand.Execute(null);
        Assert.True(cluster.IsExpanded);
        Assert.Equal("Seçimi kapat", tour.SelectText);
        cluster.Items[1].IsIncluded = false;
        Assert.Equal(2, cluster.ChosenCount);
        Assert.Equal("Seçilenleri karantinaya al (2)", tour.AllText);

        tour.SelectCommand.Execute(null);
        Assert.Equal(3, cluster.ChosenCount);
        tour.SelectCommand.Execute(null);
        foreach (var item in cluster.Items)
            item.IsIncluded = false;
        Assert.Equal(ClusterChoice.Keep, tour.Recommended);
        Assert.False(tour.QuarantineClusterCommand.CanExecute(null));
        cluster.Items[0].IsIncluded = true;
        cluster.Items[2].IsIncluded = true;

        await tour.QuarantineClusterCommand.ExecuteAsync(null);
        var sent = backend.Requests.Where(r => r.Op == Ops.Quarantine).Select(r => r.UnitId).ToList();
        Assert.Equal(["g1", "g3"], sent);
        Assert.Contains(tour.Summary!.Lines, l => l == "Yerinde kalan: 1 öğe");
    }

    [AvaloniaFact]
    public async Task Target_Plan_Runs_In_One_Session_Without_Questions()
    {
        var (vm, backend) = await Ready();
        var target = vm.Overview.Target;
        Assert.False(target.HasPlan);
        target.ChooseCommand.Execute(target.Presets[0]);
        Assert.Equal("100", target.GoalText);
        target.GoalText = "60";
        Assert.True(target.HasPlan);
        Assert.Equal(["u1"], target.Plan!.Units.Select(u => u.Id));
        var skip = Assert.Single(target.Plan.Skipped);
        Assert.Equal("u3", skip.Unit.Id);
        Assert.Equal(TargetPlan.UserDataReason, skip.Reason);
        Assert.True(target.HasShort);
        Assert.True(target.HasHeld);
        Assert.Contains("karantinada 7 gün bekler", target.NowText);
        Assert.Single(target.Rows);

        await target.ConfirmCommand.ExecuteAsync(null);
        var tour = vm.Tour;
        Assert.Null(vm.Confirm);
        Assert.True(tour.IsDone);
        Assert.Equal("Hedefli temizlik", tour.Title);
        Assert.Equal("", target.GoalText);
        var request = Assert.Single(backend.Requests, r => r.Op == Ops.Quarantine);
        Assert.Equal("u1", request.UnitId);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == "u3");
        Assert.Contains(backend.Requests, r => r.Op is Ops.Clean or Ops.SystemClean);
        var sessions = backend.Requests.Select(r => r.SessionId).Where(s => s is not null).Distinct().ToList();
        Assert.Single(sessions);
        Assert.Equal("Hedef", vm.Sessions.Last!.Title);
        Assert.StartsWith("Hedef 60 GB: ", tour.Summary!.Lines[0]);
        Assert.Contains("eksik kaldı", tour.Summary.Lines[0]);
    }

    [AvaloniaFact]
    public async Task Free_Now_Needs_Two_Presses_And_Resets_When_The_Plan_Changes()
    {
        var (vm, backend) = await Ready();
        var target = vm.Overview.Target;
        target.GoalText = "60";
        await target.FreeNowCommand.ExecuteAsync(null);
        Assert.True(target.IsPurgeArmed);
        Assert.Equal(TwoStep.ArmedText, target.FreeNowText);
        target.GoalText = "50";
        Assert.False(target.IsPurgeArmed);
        Assert.DoesNotContain(backend.Requests, r => r.UnitId == "u1");

        await target.FreeNowCommand.ExecuteAsync(null);
        await target.FreeNowCommand.ExecuteAsync(null);
        var request = Assert.Single(backend.Requests, r => r.UnitId == "u1");
        Assert.Equal(Ops.Delete, request.Op);
        Assert.Contains(vm.Tour.Summary!.Lines, l => l.StartsWith("Kalıcı silinen: 1 öğe", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Excluded_Items_Leave_The_Plan_Until_Restored()
    {
        var (vm, _) = await Ready();
        var target = vm.Overview.Target;
        target.GoalText = "60";
        target.EditCommand.Execute(null);
        Assert.Equal("Bitti", target.EditText);
        target.ExcludeCommand.Execute(target.Rows[0]);
        Assert.Empty(target.Plan!.Units);
        Assert.True(target.HasExcluded);
        Assert.False(target.HasHeld);
        target.RestoreCommand.Execute(null);
        Assert.Equal(["u1"], target.Plan!.Units.Select(u => u.Id));

        target.GoalText = "abc";
        Assert.True(target.IsInvalid);
        Assert.False(target.HasPlan);
    }

    [AvaloniaFact]
    public async Task Window_Keys_Drive_The_Cluster_Tour()
    {
        var (vm, backend) = await Ready();
        var saved = MainWindow.ContextFactory;
        MainWindow.ContextFactory = () => vm;
        try
        {
            var window = new MainWindow { Width = 1200, Height = 780 };
            window.Show();
            await Settle();
            await vm.StartTourAsync(TourMode.Ask);
            for (var i = 0; i < 10; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                await Settle();
            }
            var keys = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "TourKeys");
            Assert.True(keys.IsEffectivelyVisible);
            Assert.Contains("Esc: kalsın", keys.Text);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Settle();
            Assert.Equal("u3", vm.Tour.Cluster!.Items[0].Card.Unit.Id);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Settle();
            Assert.True(vm.Tour.Cluster!.IsExpanded);
            Assert.DoesNotContain(backend.Requests, r => r.Op == Ops.Quarantine);
            window.Close();
        }
        finally
        {
            MainWindow.ContextFactory = saved;
        }
    }
}
