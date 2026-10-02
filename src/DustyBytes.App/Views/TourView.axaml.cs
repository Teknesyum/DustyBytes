using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App.Views;

public partial class TourView : UserControl
{
    TopLevel? _top;

    public TourView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Card.PageTransition = EntryTransition.For(this);
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _top?.RemoveHandler(KeyDownEvent, OnKey);
        _top = null;
        base.OnDetachedFromVisualTree(e);
    }

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is TourViewModel { Purge.IsArmed: true } vm && !OffersView.OnPurgeButton(e.Source))
            vm.Purge.Reset();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || DataContext is not TourViewModel { IsActive: true } vm)
            return;
        if (_top?.DataContext is MainViewModel main && (main.Confirm is not null || main.Update.IsPanelOpen))
            return;
        if (_top?.FocusManager?.GetFocusedElement() is TextBox || (e.Key == Key.Enter && _top?.FocusManager?.GetFocusedElement() is Button))
            return;
        TourKey? key = e.Key switch
        {
            Key.Enter => TourKey.Enter,
            Key.Escape => TourKey.Escape,
            Key.Left => TourKey.Left,
            Key.Right => TourKey.Right,
            _ => null,
        };
        if (key is { } k && vm.Key(k))
            e.Handled = true;
    }
}
