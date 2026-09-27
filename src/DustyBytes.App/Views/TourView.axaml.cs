using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App.Views;

public partial class TourView : UserControl
{
    public TourView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Card.PageTransition = Motion(this);
    }

    public static IPageTransition? Motion(Control host)
    {
        if (TopLevel.GetTopLevel(host) is not { } top || !top.Classes.Contains("anim"))
            return null;
        if (!host.TryFindResource("TBase", out var duration) || duration is not TimeSpan span)
            return null;
        var fade = new CrossFade(span);
        if (host.TryFindResource("EOut", out var easing) && easing is Easing curve)
            fade.FadeInEasing = curve;
        return fade;
    }

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is TourViewModel { Purge.IsArmed: true } vm && !OffersView.OnPurgeButton(e.Source))
            vm.Purge.Reset();
    }
}
