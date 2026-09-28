namespace DustyBytes.App.Services;

public static class UiZoom
{
    public static readonly double[] Steps = [1.0, 1.125, 1.25, 1.375, 1.5, 1.75, 2.0];

    public static double Auto(double logicalHeight) => logicalHeight switch
    {
        >= 2000 => 1.5,
        >= 1300 => 1.25,
        _ => 1.0,
    };

    public static double Resolve(double saved, double logicalHeight) =>
        saved > 0 ? Nearest(saved) : Auto(logicalHeight);

    public static double Nearest(double zoom) => Steps.MinBy(s => Math.Abs(s - zoom));

    public static double Larger(double zoom)
    {
        var i = Array.IndexOf(Steps, Nearest(zoom));
        return Steps[Math.Min(i + 1, Steps.Length - 1)];
    }

    public static double Smaller(double zoom)
    {
        var i = Array.IndexOf(Steps, Nearest(zoom));
        return Steps[Math.Max(i - 1, 0)];
    }
}
