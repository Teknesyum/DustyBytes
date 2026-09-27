using CommunityToolkit.Mvvm.ComponentModel;

namespace DustyBytes.App.ViewModels;

public sealed partial class TwoStep : ObservableObject
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
    public const string ArmedText = "Silmek için tekrar basın";

    TimeSpan _left;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArmed))]
    private object? _target;

    public bool IsArmed => Target is not null;

    public bool IsArmedFor(object target) => ReferenceEquals(Target, target);

    public bool Press(object target)
    {
        if (ReferenceEquals(Target, target))
        {
            Target = null;
            return true;
        }
        _left = Window;
        Target = target;
        return false;
    }

    public void Reset() => Target = null;

    public void Elapse(TimeSpan elapsed)
    {
        if (Target is null)
            return;
        _left -= elapsed;
        if (_left <= TimeSpan.Zero)
            Target = null;
    }
}
