using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DustyBytes.App.ViewModels;

public sealed partial class ToastViewModel : ObservableObject
{
    readonly Func<Task>? _action;
    readonly Action<ToastViewModel> _close;

    public ToastViewModel(string message, bool isError, string? actionText, Func<Task>? action, TimeSpan life, Action<ToastViewModel> close)
    {
        Message = message;
        IsError = isError;
        ActionText = actionText ?? "";
        _action = action;
        Remaining = life;
        _close = close;
    }

    public string Message { get; }
    public bool IsError { get; }
    public string ActionText { get; }
    public bool HasAction => _action is not null;
    public TimeSpan Remaining { get; private set; }

    [ObservableProperty]
    private bool _isPaused;

    public bool Elapse(TimeSpan elapsed)
    {
        if (IsError || IsPaused)
            return false;
        Remaining -= elapsed;
        return Remaining <= TimeSpan.Zero;
    }

    [RelayCommand]
    private void Dismiss() => _close(this);

    [RelayCommand]
    private async Task Act()
    {
        _close(this);
        if (_action is not null)
            await _action();
    }
}

public sealed partial class ConfirmViewModel : ObservableObject
{
    readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConfirmViewModel(string title, string message, string confirmText, bool isDanger)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        IsDanger = isDanger;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public bool IsDanger { get; }
    public Task<bool> Result => _result.Task;

    [RelayCommand]
    private void Accept() => _result.TrySetResult(true);

    [RelayCommand]
    private void Decline() => _result.TrySetResult(false);
}

public sealed class NavItem(string label, string glyph, ViewModelBase screen)
{
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public ViewModelBase Screen { get; } = screen;
}
