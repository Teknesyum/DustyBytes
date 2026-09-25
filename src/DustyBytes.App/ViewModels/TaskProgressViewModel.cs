using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;

namespace DustyBytes.App.ViewModels;

public sealed partial class LogLine(string text) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty]
    private bool _isNewest = true;
}

public sealed partial class TaskProgressViewModel : ObservableObject
{
    public const int MaxLines = 9;
    public const double Ceiling = 0.99;
    static readonly TimeSpan LineGap = TimeSpan.FromMilliseconds(160);

    readonly Action<TaskProgressViewModel, bool>? _register;
    CancellationTokenSource? _cts;
    DateTime _lastLine = DateTime.MinValue;
    double _stepCeiling = Ceiling;
    bool _measured;

    public TaskProgressViewModel(Action<TaskProgressViewModel, bool>? register = null) => _register = register;

    public ObservableCollection<LogLine> Lines { get; } = [];

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _step = "";

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private string _percentText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _canCancel;

    public async Task<T> RunAsync<T>(string title, Func<IProgress<TaskStep>, CancellationToken, Task<T>> work, bool cancellable = true)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Title = title;
        Step = title;
        Value = 0;
        _stepCeiling = Ceiling;
        _measured = false;
        PercentText = "%0";
        Lines.Clear();
        CanCancel = cancellable;
        IsRunning = true;
        _register?.Invoke(this, true);
        try
        {
            var result = await work(new Progress<TaskStep>(Report), token);
            Advance(1);
            return result;
        }
        finally
        {
            IsRunning = false;
            CanCancel = false;
            _register?.Invoke(this, false);
        }
    }

    public void Report(TaskStep step)
    {
        if (!IsRunning)
            return;
        if (!string.IsNullOrWhiteSpace(step.Step) && step.Step != Step)
        {
            Step = step.Step;
            _stepCeiling = Math.Min(Ceiling, Value + (Ceiling - Value) * 0.5);
        }
        if (step.Percent is >= 0 and <= 100 && !double.IsNaN(step.Percent))
        {
            _measured = true;
            Advance(Math.Min(Ceiling, step.Percent / 100.0));
        }
        if (!string.IsNullOrWhiteSpace(step.Line))
            AddLine(step.Line!);
    }

    public void AddLine(string text, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastLine < LineGap && Lines.Count > 0)
            return;
        _lastLine = now;
        if (Lines.Count > 0)
            Lines[^1].IsNewest = false;
        Lines.Add(new LogLine(text));
        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);
    }

    public void Tick()
    {
        if (!IsRunning || _measured)
            return;
        var ceiling = Math.Max(_stepCeiling, Value);
        Advance(Value + (ceiling - Value) * 0.02);
    }

    void Advance(double target)
    {
        if (target <= Value)
            return;
        Value = target;
        PercentText = "%" + ((int)Math.Floor(Value * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        CanCancel = false;
        Step = "İptal ediliyor";
    }
}
