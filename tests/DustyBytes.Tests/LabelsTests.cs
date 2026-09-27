using Avalonia.Headless.XUnit;
using DustyBytes.App.Services;

namespace DustyBytes.Tests;

public class LabelsTests
{
    [AvaloniaFact]
    public void TitleBarTextsComeFromGeneratedLabels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "teknesyum-ui", "avalonia", "labels.tr.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        Assert.Equal(r.GetProperty("sig.brand").GetString(), Labels.Brand);
        Assert.Equal(r.GetProperty("sig.support").GetString(), Labels.Support);
        Assert.Equal(r.GetProperty("update.label").GetString(), Labels.Update);
    }
}
