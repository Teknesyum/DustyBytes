using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace DustyBytes.App.Controls;

public sealed class TransformAnimator : InterpolatingAnimator<ITransform>
{
    static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        Animation.RegisterCustomAnimator<ITransform, TransformAnimator>();
    }

    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue)
    {
        var from = oldValue as TransformOperations ?? TransformOperations.Identity;
        var to = newValue as TransformOperations ?? TransformOperations.Identity;
        return TransformOperations.Interpolate(from, to, progress);
    }
}
