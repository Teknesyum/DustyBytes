using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, true);
    }

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is OverviewViewModel { Purge.IsArmed: true } vm && !OffersView.OnPurgeButton(e.Source))
            vm.Purge.Reset();
    }
}
