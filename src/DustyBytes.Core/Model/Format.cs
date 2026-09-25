using System.Globalization;

namespace DustyBytes.Core.Model;

public static class Format
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, Tr) + " " + Units[unit];
    }

    public static string Count(long count) => count.ToString("N0", Tr);

    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var days = (now - then).TotalDays;
        if (days < 1) return "bugün";
        if (days < 2) return "dün";
        if (days < 31) return $"{(int)days} gün önce";
        if (days < 365) return $"{(int)(days / 30.44)} ay önce";
        var years = days / 365.25;
        return years < 2 ? $"{(int)(days / 30.44)} ay önce" : $"{(int)years} yıl önce";
    }
}
