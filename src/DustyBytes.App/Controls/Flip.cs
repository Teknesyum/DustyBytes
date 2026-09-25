using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace DustyBytes.App.Controls;

public static class Flip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(Flip));

    static Flip() => IsEnabledProperty.Changed.AddClassHandler<Control>(OnChanged);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= Attach;
        if (e.NewValue is true)
        {
            control.AttachedToVisualTree += Attach;
            if (TopLevel.GetTopLevel(control) is not null)
                Apply(control);
        }
    }

    static void Attach(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Apply(control);
    }

    static void Apply(Control control)
    {
        if (TopLevel.GetTopLevel(control) is not Window window || !window.Classes.Contains("anim"))
            return;
        if (ElementComposition.GetElementVisual(control) is not { } visual)
            return;
        var compositor = visual.Compositor;
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        offset.InsertExpressionKeyFrame(1f, "this.FinalValue");
        offset.Duration = window.TryFindResource("TBase", out var d) && d is TimeSpan span ? span : TimeSpan.FromMilliseconds(240);
        var animations = compositor.CreateImplicitAnimationCollection();
        animations["Offset"] = offset;
        visual.ImplicitAnimations = animations;
    }
}
