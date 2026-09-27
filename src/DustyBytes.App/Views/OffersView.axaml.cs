using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DustyBytes.App.ViewModels;

namespace DustyBytes.App.Views;

public partial class OffersView : UserControl
{
    public OffersView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, true);
    }

    public static bool OnPurgeButton(object? source) =>
        source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any(b => b.Classes.Contains("purge"));

    void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is OffersViewModel { Purge.IsArmed: true } vm && !OnPurgeButton(e.Source))
            vm.Disarm();
    }
}
