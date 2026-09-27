using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DustyBytes.App.Views;

public sealed class EntryTransition(TimeSpan duration, Easing enter, Easing exit, double offset) : IPageTransition
{
    public TimeSpan Duration { get; } = duration;
    public double Offset { get; } = offset;

    public static EntryTransition? For(Control host)
    {
        if (TopLevel.GetTopLevel(host) is not { } top || !top.Classes.Contains("anim"))
            return null;
        if (!host.TryFindResource("TBase", out var duration) || duration is not TimeSpan span
            || !host.TryFindResource("EOut", out var enter) || enter is not Easing inCurve
            || !host.TryFindResource("EIn", out var exit) || exit is not Easing outCurve
            || !host.TryFindResource("EntryOffset", out var offset) || offset is not double rise)
            return null;
        return new EntryTransition(span, inCurve, outCurve, rise);
    }

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        var running = new List<Task>();
        if (from is not null)
            running.Add(Build(1, 0, 0, exit).RunAsync(from, cancellationToken));
        if (to is not null)
        {
            to.IsVisible = true;
            running.Add(Build(0, 1, Offset, enter).RunAsync(to, cancellationToken));
        }
        await Task.WhenAll(running);
        if (from is not null && !cancellationToken.IsCancellationRequested)
            from.IsVisible = false;
    }

    Animation Build(double fromOpacity, double toOpacity, double rise, Easing curve) => new()
    {
        Duration = Duration,
        Easing = curve,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.YProperty, rise) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.YProperty, 0d) } },
        },
    };
}
