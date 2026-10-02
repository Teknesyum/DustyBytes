using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App.Views;

public partial class QuarantineView : UserControl
{
    public QuarantineView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, true);
    }

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuarantineViewModel { Empty.IsArmed: true } vm && !OffersView.OnPurgeButton(e.Source))
            vm.Empty.Reset();
    }
}
