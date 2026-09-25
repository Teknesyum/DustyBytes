using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DustyBytes.App.Views;

public partial class SignatureView : UserControl
{
    public SignatureView() => InitializeComponent();

    async void OnSignatureNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(uri);
    }
}
