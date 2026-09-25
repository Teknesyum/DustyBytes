using CommunityToolkit.Mvvm.ComponentModel;

namespace DustyBytes.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public bool IsActive { get; private set; }

    public void NavigatedTo()
    {
        IsActive = true;
        OnNavigatedTo();
    }

    public void NavigatedFrom()
    {
        IsActive = false;
        OnNavigatedFrom();
    }

    protected virtual void OnNavigatedTo()
    {
    }

    protected virtual void OnNavigatedFrom()
    {
    }
}

public sealed partial class NavigationStack : ObservableObject
{
    readonly Stack<ViewModelBase> _back = new();

    [ObservableProperty]
    private ViewModelBase? _current;

    public bool CanPop => _back.Count > 0;

    public int Depth => _back.Count;

    public void Navigate(ViewModelBase target)
    {
        if (ReferenceEquals(Current, target) && _back.Count == 0)
            return;
        Current?.NavigatedFrom();
        _back.Clear();
        Show(target);
    }

    public void Push(ViewModelBase target)
    {
        if (Current is { } current)
        {
            current.NavigatedFrom();
            _back.Push(current);
        }
        Show(target);
    }

    public bool Pop()
    {
        if (_back.Count == 0)
            return false;
        Current?.NavigatedFrom();
        Show(_back.Pop());
        return true;
    }

    void Show(ViewModelBase target)
    {
        Current = target;
        OnPropertyChanged(nameof(CanPop));
        OnPropertyChanged(nameof(Depth));
        target.NavigatedTo();
    }
}
