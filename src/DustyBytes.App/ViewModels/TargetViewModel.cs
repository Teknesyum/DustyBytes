using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed record TargetPreset(string Label, int Gigabytes);

public sealed record PlanRow(Unit Unit, string Name, string Detail, string SizeText);

public sealed record SkipRow(string Name, string Detail, string SizeText);

public sealed partial class TargetViewModel : ObservableObject
{
    public const int SkipShown = 5;

    readonly MainViewModel _main;
    readonly OverviewViewModel _overview;
    readonly HashSet<string> _excluded = [];
    string _signature = "";
    object _token = new();

    public TargetViewModel(MainViewModel main, OverviewViewModel overview)
    {
        _main = main;
        _overview = overview;
        Purge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TwoStep.Target))
            {
                OnPropertyChanged(nameof(IsPurgeArmed));
                OnPropertyChanged(nameof(FreeNowText));
            }
        };
        main.Ticked += Purge.Elapse;
    }

    public IReadOnlyList<TargetPreset> Presets { get; } =
    [
        new("Yeni oyun için 100 GB", 100),
        new("Video projesi için 50 GB", 50),
        new("Windows güncellemesi için 20 GB", 20),
    ];

    public TwoStep Purge { get; } = new();
    public ObservableCollection<PlanRow> Rows { get; } = [];
    public ObservableCollection<SkipRow> Skipped { get; } = [];

    [ObservableProperty]
    private string _goalText = "";

    [ObservableProperty]
    private TargetPlan? _plan;

    [ObservableProperty]
    private bool _isEditing;

    public long? Goal => Parse(GoalText);
    public bool HasGoal => Goal is not null;
    public bool HasPlan => Plan is not null;
    public bool IsWaiting => HasGoal && Plan is null;
    public string WaitText => _overview.IsWaitingScan ? OverviewViewModel.WaitText : "Güvenli küme ölçülüyor; plan birazdan hazır";
    public bool IsInvalid => GoalText.Trim().Length > 0 && !HasGoal;
    public string InvalidText => "Gigabayt olarak bir sayı yazın, örneğin 60";
    public string HeadText => Plan?.HeadText ?? "";
    public string NowText => Plan?.NowText ?? "";
    public string ShortText => Plan?.ShortText ?? "";
    public bool HasShort => Plan is { IsMet: false };
    public bool HasRows => Rows.Count > 0;
    public bool HasSkipped => Skipped.Count > 0;
    public string MoreSkippedText => Plan is { } p && p.Skipped.Count > SkipShown ? $"ve {Format.Count(p.Skipped.Count - SkipShown)} öğe daha" : "";
    public bool HasMoreSkipped => MoreSkippedText.Length > 0;
    public bool HasHeld => Plan is { HeldBytes: > 0 };
    public bool HasExcluded => _excluded.Count > 0;
    public string EditText => IsEditing ? "Bitti" : "Değiştir";
    public bool IsPurgeArmed => Purge.IsArmed;
    public string FreeNowText => IsPurgeArmed ? TwoStep.ArmedText : "Şimdi yer aç";
    public string FreeNowTip => "Plandakileri karantinaya almadan kalıcı siler; geri alınamaz";
    public string ConfirmTip => "Plan tek oturumda uygulanır; büyük öğeler karantinaya gider";

    public static long? Parse(string? text)
    {
        var clean = (text ?? "").Trim();
        if (clean.EndsWith("gb", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^2].Trim();
        clean = clean.Replace(',', '.');
        if (!double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > 1_000_000)
            return null;
        return (long)Math.Round(value * (1L << 30));
    }

    partial void OnGoalTextChanged(string value)
    {
        OnPropertyChanged(nameof(Goal));
        OnPropertyChanged(nameof(HasGoal));
        OnPropertyChanged(nameof(IsInvalid));
        Refresh();
    }

    partial void OnPlanChanged(TargetPlan? value)
    {
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(HeadText));
        OnPropertyChanged(nameof(NowText));
        OnPropertyChanged(nameof(ShortText));
        OnPropertyChanged(nameof(HasShort));
        OnPropertyChanged(nameof(HasHeld));
        OnPropertyChanged(nameof(MoreSkippedText));
        OnPropertyChanged(nameof(HasMoreSkipped));
        ConfirmCommand.NotifyCanExecuteChanged();
        FreeNowCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(EditText));

    bool Ready() => _main.Session.HasSnapshot && !_overview.IsWaitingScan && _overview.IsSafeKnown;

    public void Refresh()
    {
        OnPropertyChanged(nameof(WaitText));
        if (Goal is not { } goal || !Ready())
        {
            Apply(null);
            return;
        }
        var units = _main.Session.Snapshot?.Units ?? [];
        Apply(TargetPlanner.Build(goal, _overview.SafeBytes, units, DateTimeOffset.Now, _excluded));
    }

    void Apply(TargetPlan? plan)
    {
        var signature = plan is null ? "" : $"{plan.Target}|{plan.SafeBytes}|{string.Join(",", plan.Units.Select(u => u.Id))}|{string.Join(",", plan.Skipped.Select(s => s.Unit.Id))}";
        if (signature != _signature)
        {
            _signature = signature;
            _token = new object();
            Purge.Reset();
            Rows.Clear();
            Skipped.Clear();
            if (plan is not null)
            {
                foreach (var u in plan.Units)
                    Rows.Add(new PlanRow(u, u.Name, $"{KindText.Label(u)} · {KindText.Usage(u.Usage, plan.Now)}", Format.Bytes(u.SizeBytes)));
                foreach (var s in plan.Skipped.Take(SkipShown))
                    Skipped.Add(new SkipRow(s.Unit.Name, $"{KindText.Label(s.Unit)} · {s.Reason}", Format.Bytes(s.Unit.SizeBytes)));
            }
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasSkipped));
        }
        Plan = plan;
        OnPropertyChanged(nameof(IsWaiting));
        ConfirmCommand.NotifyCanExecuteChanged();
        FreeNowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Choose(TargetPreset? preset)
    {
        if (preset is not null)
            GoalText = preset.Gigabytes.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void Edit() => IsEditing = !IsEditing;

    [RelayCommand]
    private void Exclude(PlanRow? row)
    {
        if (row is null || !_excluded.Add(row.Unit.Id))
            return;
        OnPropertyChanged(nameof(HasExcluded));
        Refresh();
    }

    [RelayCommand]
    private void Restore()
    {
        _excluded.Clear();
        OnPropertyChanged(nameof(HasExcluded));
        Refresh();
    }

    bool CanRun() => Plan is { IsEmpty: false } && Ready() && !_main.Tour.IsActive && !_main.Session.Scan.IsRunning && !_main.Session.IsRestoring;

    bool CanFreeNow() => CanRun() && HasHeld;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Confirm() => RunAsync(false);

    [RelayCommand(CanExecute = nameof(CanFreeNow))]
    private Task FreeNow() => Purge.Press(_token) ? RunAsync(true) : Task.CompletedTask;

    public void Disarm() => Purge.Reset();

    async Task RunAsync(bool purge)
    {
        if (Plan is not { } plan || !CanRun())
            return;
        if (_main.IsBusy)
        {
            _main.Notify("Başka bir işlem sürüyor; bitince yeniden deneyin");
            return;
        }
        Purge.Reset();
        IsEditing = false;
        _excluded.Clear();
        OnPropertyChanged(nameof(HasExcluded));
        _main.Navigation.Push(_main.Tour);
        GoalText = "";
        await _main.Tour.StartPlanAsync(plan, purge);
    }
}
