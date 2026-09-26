using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using DustyBytes.App.Treemap;
using DustyBytes.App.ViewModels;
using DustyBytes.App.Views;
using SkiaSharp;

namespace DustyBytes.App.Controls;

public sealed class TreemapControl : Control
{
    public const double MinTile = 24;
    const double Gap = 2;

    public static readonly StyledProperty<IReadOnlyList<MapEntry>?> EntriesProperty =
        AvaloniaProperty.Register<TreemapControl, IReadOnlyList<MapEntry>?>(nameof(Entries));

    public static readonly StyledProperty<MapEntry?> SelectedProperty =
        AvaloniaProperty.Register<TreemapControl, MapEntry?>(nameof(Selected), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> EnterCommandProperty =
        AvaloniaProperty.Register<TreemapControl, ICommand?>(nameof(EnterCommand));

    public static readonly StyledProperty<ICommand?> UpCommandProperty =
        AvaloniaProperty.Register<TreemapControl, ICommand?>(nameof(UpCommand));

    List<Tile<MapEntry>> _tiles = [];
    Size _laidOut;

    static TreemapControl()
    {
        FocusableProperty.OverrideDefaultValue<TreemapControl>(true);
        AffectsRender<TreemapControl>(EntriesProperty, SelectedProperty);
    }

    public IReadOnlyList<MapEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public MapEntry? Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public ICommand? EnterCommand
    {
        get => GetValue(EnterCommandProperty);
        set => SetValue(EnterCommandProperty, value);
    }

    public ICommand? UpCommand
    {
        get => GetValue(UpCommandProperty);
        set => SetValue(UpCommandProperty, value);
    }

    public IReadOnlyList<Tile<MapEntry>> Tiles => _tiles;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EntriesProperty)
            Relayout(Bounds.Size, force: true);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Relayout(finalSize, force: false);
        return finalSize;
    }

    void Relayout(Size size, bool force)
    {
        if (!force && size == _laidOut)
            return;
        _laidOut = size;
        var entries = Entries ?? [];
        var area = size.Width * size.Height;
        var items = entries.Select(e => (e, (double)e.Bytes)).ToList();
        var total = items.Sum(i => i.Item2);
        var grouped = Squarify.GroupSmall(items, total, area, MinTile * MinTile, MapViewModel.Others);
        _tiles = Squarify.Layout(grouped, new TileRect(0, 0, size.Width, size.Height));
        InvalidateVisual();
    }

    public MapEntry? HitTest(Point p) => _tiles.FirstOrDefault(t => t.Rect.Contains(p.X, p.Y)).Item;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        var hit = HitTest(e.GetPosition(this));
        if (hit is null)
            return;
        if (e.ClickCount >= 2 && EnterCommand?.CanExecute(hit) == true)
            EnterCommand.Execute(hit);
        else
            Selected = hit;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_tiles.Count == 0)
            return;
        var index = Selected is null ? -1 : _tiles.FindIndex(t => ReferenceEquals(t.Item, Selected));
        switch (e.Key)
        {
            case Key.Right or Key.Down:
                Selected = _tiles[(index + 1) % _tiles.Count].Item;
                break;
            case Key.Left or Key.Up:
                Selected = _tiles[index <= 0 ? _tiles.Count - 1 : index - 1].Item;
                break;
            case Key.Enter when Selected is not null && EnterCommand?.CanExecute(Selected) == true:
                EnterCommand.Execute(Selected);
                break;
            case Key.Back when UpCommand?.CanExecute(null) == true:
                UpCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var tiles = _tiles.Select(t => new DrawTile(Snap(t.Rect, scaling), t.Item.Name, t.Item.SizeText, t.Item.Kind, t.Item.IsOthers, ReferenceEquals(t.Item, Selected))).ToList();
        var palette = new Palette(
            ColorOf("Renk1"), ColorOf("Renk2Text"), ColorOf("Renk3Text"), ColorOf("Success"), ColorOf("Warning"),
            ColorOf("BorderDecorative"), ColorOf("Surface"), ColorOf("TextBody"));
        context.Custom(new TreemapDraw(new Rect(Bounds.Size), tiles, palette, Selected is not null, FamilyOf("FontSans", "Segoe UI"), FamilyOf("FontMono", "Consolas")));
    }

    static string[] FamilyOf(string key, string fallback) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is FontFamily f
            ? [.. f.FamilyNames.Select(n => n.Trim()), fallback]
            : [fallback];

    static SKTypeface Typeface(string[] families, SKFontStyle style)
    {
        foreach (var name in families)
        {
            var face = SKTypeface.FromFamilyName(name, style);
            if (face is not null && string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase))
                return face;
            face?.Dispose();
        }
        return SKTypeface.FromFamilyName(families[^1], style) ?? SKTypeface.Default;
    }

    static TileRect Snap(TileRect r, double s)
    {
        double S(double v) => Math.Round(v * s) / s;
        var x = S(r.X);
        var y = S(r.Y);
        return new TileRect(x, y, Math.Max(0, S(r.Right) - x - Gap), Math.Max(0, S(r.Bottom) - y - Gap));
    }

    static SKColor ColorOf(string key) =>
        UiConvert.Resource(key) is ISolidColorBrush b ? new SKColor(b.Color.R, b.Color.G, b.Color.B, b.Color.A) : SKColors.Transparent;

    sealed record DrawTile(TileRect Rect, string Name, string Size, Core.Model.UnitKind? Kind, bool Others, bool Selected);

    sealed record Palette(SKColor Blue, SKColor Pink, SKColor Purple, SKColor Success, SKColor Warning, SKColor Other, SKColor Surface, SKColor Text);

    sealed class TreemapDraw(Rect bounds, List<DrawTile> tiles, Palette palette, bool dim, string[] sans, string[] mono) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null)
                return;
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            using var typeface = Typeface(sans, SKFontStyle.Bold);
            using var font = new SKFont(typeface, 14);
            using var monoFace = Typeface(mono, SKFontStyle.Bold);
            using var monoFont = new SKFont(monoFace, 14);
            using var text = new SKPaint { IsAntialias = true };
            foreach (var t in tiles)
            {
                var rect = SKRect.Create((float)t.Rect.X, (float)t.Rect.Y, (float)t.Rect.Width, (float)t.Rect.Height);
                if (rect.Width < 1 || rect.Height < 1)
                    continue;
                var (key, filled) = UiConvert.KindStyle(t.Kind);
                var color = key switch
                {
                    "Renk1" => palette.Blue,
                    "Renk2Text" => palette.Pink,
                    "Renk3Text" => palette.Purple,
                    "Success" => palette.Success,
                    "Warning" => palette.Warning,
                    _ => palette.Other,
                };
                var alpha = dim && !t.Selected ? 0.45f : 1f;
                var round = new SKRoundRect(rect, 6, 6);
                if (filled)
                {
                    fill.Color = color.WithAlpha((byte)(color.Alpha * alpha));
                    canvas.DrawRoundRect(round, fill);
                }
                else
                {
                    fill.Color = palette.Surface;
                    canvas.DrawRoundRect(round, fill);
                    stroke.Color = color.WithAlpha((byte)(255 * alpha));
                    var inset = new SKRoundRect(SKRect.Inflate(rect, -1, -1), 6, 6);
                    canvas.DrawRoundRect(inset, stroke);
                }
                if (t.Selected)
                {
                    stroke.Color = palette.Text;
                    canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -1, -1), 6, 6), stroke);
                }
                if (rect.Width < 72 || rect.Height < 44)
                    continue;
                var dark = filled && key != "BorderDecorative";
                text.Color = (dark ? SKColors.Black : palette.Text).WithAlpha((byte)(255 * alpha));
                var max = rect.Width - 16;
                canvas.DrawText(Fit(t.Name, font, max), rect.Left + 8, rect.Top + 20, font, text);
                canvas.DrawText(Fit(t.Size, monoFont, max), rect.Left + 8, rect.Top + 38, monoFont, text);
            }
        }

        static float Measure(string value, SKFont font)
        {
            using var paint = new SKPaint(font);
            return paint.MeasureText(value);
        }

        static string Fit(string value, SKFont font, float max)
        {
            if (Measure(value, font) <= max)
                return value;
            for (var n = value.Length - 1; n > 0; n--)
            {
                var candidate = value[..n] + "…";
                if (Measure(candidate, font) <= max)
                    return candidate;
            }
            return "";
        }
    }
}
