using Avalonia;
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
        Card.PageTransition = EntryTransition.For(this);
    }

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is TourViewModel { Purge.IsArmed: true } vm && !OffersView.OnPurgeButton(e.Source))
            vm.Purge.Reset();
    }
}
