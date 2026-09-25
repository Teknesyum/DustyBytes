using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using DustyBytes.Core.Model;

namespace DustyBytes.App.Views;

public static class UiConvert
{
    public static readonly IValueConverter ScaleX = new FuncValueConverter<double, ITransform>(v =>
        TransformOperations.Parse("scaleX(" + Math.Clamp(double.IsNaN(v) ? 0 : v, 0, 1).ToString("0.####", CultureInfo.InvariantCulture) + ")"));

    public static readonly IValueConverter KindFill = new FuncValueConverter<UnitKind?, IBrush?>(k => KindBrush(k, fill: true));

    public static readonly IValueConverter KindStroke = new FuncValueConverter<UnitKind?, IBrush?>(k => KindBrush(k, fill: false));

    public static IBrush? Resource(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true ? value as IBrush : null;

    public static IBrush? KindBrush(UnitKind? kind, bool fill)
    {
        var (key, filled) = KindStyle(kind);
        return fill && !filled ? Brushes.Transparent : Resource(key);
    }

    public static (string Key, bool Filled) KindStyle(UnitKind? kind) => kind switch
    {
        UnitKind.Game => ("NeonBlue", true),
        UnitKind.Film or UnitKind.Series => ("PinkText", true),
        UnitKind.Program => ("PurpleText", false),
        UnitKind.DevArtifact => ("Success", true),
        UnitKind.Cache or UnitKind.BrowserCache => ("Warning", false),
        _ => ("BorderDecorative", true),
    };
}
