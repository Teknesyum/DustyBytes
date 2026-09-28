using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DustyBytes.App;
using DustyBytes.App.Services;

namespace DustyBytes.Tests;

public sealed class UiZoomTests
{
    [Theory]
    [InlineData(1040, 1.0)]
    [InlineData(1392, 1.25)]
    [InlineData(2100, 1.5)]
    public void Auto_Grows_With_Logical_Screen_Height(double height, double expected) =>
        Assert.Equal(expected, UiZoom.Auto(height));

    [Fact]
    public void Saved_Zoom_Wins_Over_Auto() =>
        Assert.Equal(1.5, UiZoom.Resolve(1.5, 1040));

    [Fact]
    public void Steps_Stop_At_The_Ends()
    {
        Assert.Equal(2.0, UiZoom.Larger(2.0));
        Assert.Equal(1.0, UiZoom.Smaller(1.0));
        Assert.Equal(1.375, UiZoom.Larger(1.25));
    }

    [AvaloniaFact]
    public void SetZoom_Scales_The_Whole_Window_And_Its_Minimum()
    {
        var window = new MainWindow();
        var min = window.MinWidth;
        window.SetZoom(1.25);
        var host = window.FindControl<LayoutTransformControl>("Zoom")!;
        var scale = Assert.IsType<ScaleTransform>(host.LayoutTransform);
        Assert.Equal(1.25, scale.ScaleX);
        Assert.Equal(min * 1.25, window.MinWidth);
        window.SetZoom(1.0);
        Assert.Null(host.LayoutTransform);
    }

    [AvaloniaFact]
    public void Sidebar_Buttons_Step_The_Zoom()
    {
        var window = new MainWindow();
        window.Show();
        var zoomIn = window.FindControl<Button>("ZoomIn")!;
        var zoomOut = window.FindControl<Button>("ZoomOut")!;
        Assert.False(zoomOut.IsEnabled);
        zoomIn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.125, window.ZoomLevel);
        Assert.Equal("%113", window.FindControl<TextBlock>("ZoomText")!.Text);
        Assert.True(zoomOut.IsEnabled);
        window.Close();
    }
}
