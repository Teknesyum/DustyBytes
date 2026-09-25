namespace DustyBytes.App.Treemap;

public readonly record struct TileRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double px, double py) => px >= X && px < Right && py >= Y && py < Bottom;
}

public readonly record struct Tile<T>(T Item, double Value, TileRect Rect);

public static class Squarify
{
    public static readonly double Phi = (1 + Math.Sqrt(5)) / 2;

    public static List<Tile<T>> Layout<T>(IReadOnlyList<(T Item, double Value)> items, TileRect bounds, double ratio = 0)
    {
        if (ratio <= 1)
            ratio = Phi;
        var nodes = items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).ToList();
        var result = new List<Tile<T>>(nodes.Count);
        if (nodes.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return result;

        double x0 = bounds.X, y0 = bounds.Y, x1 = bounds.Right, y1 = bounds.Bottom;
        var value = nodes.Sum(n => n.Value);
        int i0 = 0, i1 = 0, n = nodes.Count;

        while (i0 < n)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double sumValue;
            do sumValue = nodes[i1++].Value; while (sumValue == 0 && i1 < n);
            double minValue = sumValue, maxValue = sumValue;
            var alpha = Math.Max(dy / dx, dx / dy) / (value * ratio);
            var beta = sumValue * sumValue * alpha;
            var minRatio = Math.Max(maxValue / beta, beta / minValue);

            for (; i1 < n; ++i1)
            {
                var nodeValue = nodes[i1].Value;
                sumValue += nodeValue;
                if (nodeValue < minValue) minValue = nodeValue;
                if (nodeValue > maxValue) maxValue = nodeValue;
                beta = sumValue * sumValue * alpha;
                var newRatio = Math.Max(maxValue / beta, beta / minValue);
                if (newRatio > minRatio)
                {
                    sumValue -= nodeValue;
                    break;
                }
                minRatio = newRatio;
            }

            var dice = dx < dy;
            if (dice)
            {
                var yEnd = dy > 0 ? y0 + dy * sumValue / value : y1;
                Dice(nodes, i0, i1, sumValue, x0, y0, x1, yEnd, result);
                y0 = yEnd;
            }
            else
            {
                var xEnd = dx > 0 ? x0 + dx * sumValue / value : x1;
                Slice(nodes, i0, i1, sumValue, x0, y0, xEnd, y1, result);
                x0 = xEnd;
            }
            value -= sumValue;
            i0 = i1;
        }
        return result;
    }

    static void Dice<T>(List<(T Item, double Value)> nodes, int from, int to, double sum, double x0, double y0, double x1, double y1, List<Tile<T>> output)
    {
        var k = sum > 0 ? (x1 - x0) / sum : 0;
        var x = x0;
        for (var i = from; i < to; i++)
        {
            var w = nodes[i].Value * k;
            output.Add(new Tile<T>(nodes[i].Item, nodes[i].Value, new TileRect(x, y0, w, y1 - y0)));
            x += w;
        }
    }

    static void Slice<T>(List<(T Item, double Value)> nodes, int from, int to, double sum, double x0, double y0, double x1, double y1, List<Tile<T>> output)
    {
        var k = sum > 0 ? (y1 - y0) / sum : 0;
        var y = y0;
        for (var i = from; i < to; i++)
        {
            var h = nodes[i].Value * k;
            output.Add(new Tile<T>(nodes[i].Item, nodes[i].Value, new TileRect(x0, y, x1 - x0, h)));
            y += h;
        }
    }

    public static List<(T Item, double Value)> GroupSmall<T>(IReadOnlyList<(T Item, double Value)> items, double total, double area, double minTileArea, Func<IReadOnlyList<T>, T> makeOthers)
    {
        if (total <= 0 || area <= 0)
            return items.ToList();
        var threshold = minTileArea / area * total;
        var big = new List<(T, double)>();
        var small = new List<T>();
        double smallSum = 0;
        foreach (var (item, value) in items)
        {
            if (value >= threshold)
                big.Add((item, value));
            else if (value > 0)
            {
                small.Add(item);
                smallSum += value;
            }
        }
        if (small.Count == 1 && smallSum > 0)
            big.Add((small[0], smallSum));
        else if (small.Count > 1)
            big.Add((makeOthers(small), smallSum));
        return big;
    }
}
