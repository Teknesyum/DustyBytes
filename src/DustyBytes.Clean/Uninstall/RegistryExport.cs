using System.Globalization;
using System.Text;

namespace DustyBytes.Clean.Uninstall;

public sealed record RegExportItem(RegKeyRef Key, string? ValueName = null);

public static class RegistryExport
{
    public const string Header = "Windows Registry Editor Version 5.00";
    const int LineWidth = 80;

    public static void ToRegFile(IRegistryView reg, IEnumerable<RegKeyRef> keys, string path) =>
        ToRegFile(reg, keys.Select(k => new RegExportItem(k)), path);

    public static void ToRegFile(IRegistryView reg, IEnumerable<RegExportItem> items, string path)
    {
        var text = Render(reg, items);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, text, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
    }

    public static string Render(IRegistryView reg, IEnumerable<RegExportItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n\r\n");
        foreach (var item in items)
        {
            if (item.ValueName is { } valueName)
            {
                var v = reg.GetValue(item.Key, valueName);
                if (v is null)
                    continue;
                sb.Append('[').Append(item.Key.PhysicalPath).Append("]\r\n");
                AppendValue(sb, v);
                sb.Append("\r\n");
            }
            else
            {
                AppendKey(sb, reg, item.Key, 0);
            }
        }
        return sb.ToString();
    }

    static void AppendKey(StringBuilder sb, IRegistryView reg, RegKeyRef key, int depth)
    {
        if (depth > 64 || !reg.KeyExists(key))
            return;
        sb.Append('[').Append(key.PhysicalPath).Append("]\r\n");
        var names = reg.GetValueNames(key).OrderBy(n => n.Length == 0 ? 0 : 1).ToList();
        foreach (var name in names)
            if (reg.GetValue(key, name) is { } v)
                AppendValue(sb, v);
        sb.Append("\r\n");
        foreach (var sub in reg.GetSubKeyNames(key))
            AppendKey(sb, reg, key.Child(sub), depth + 1);
    }

    public static void AppendValue(StringBuilder sb, RegValue v)
    {
        var prefix = (v.Name.Length == 0 ? "@" : "\"" + Escape(v.Name) + "\"") + "=";
        switch (v.Kind)
        {
            case RegKind.String when v.Data is string s && !s.Contains('\0') && !s.Contains('\n') && !s.Contains('\r'):
                sb.Append(prefix).Append('"').Append(Escape(s)).Append("\"\r\n");
                break;
            case RegKind.String:
                AppendHex(sb, prefix + "hex(1):", StringBytes(v.Data as string ?? ""));
                break;
            case RegKind.ExpandString:
                AppendHex(sb, prefix + "hex(2):", StringBytes(v.Data as string ?? ""));
                break;
            case RegKind.MultiString:
                AppendHex(sb, prefix + "hex(7):", MultiBytes(v.Data as string[] ?? []));
                break;
            case RegKind.DWord:
                var dw = v.Data switch { int i => unchecked((uint)i), uint u => u, long l => unchecked((uint)l), _ => 0u };
                sb.Append(prefix).Append("dword:").Append(dw.ToString("x8", CultureInfo.InvariantCulture)).Append("\r\n");
                break;
            case RegKind.QWord:
                var qw = v.Data switch { long l => l, int i => i, ulong u => unchecked((long)u), _ => 0L };
                AppendHex(sb, prefix + "hex(b):", BitConverter.GetBytes(qw));
                break;
            case RegKind.Binary:
                AppendHex(sb, prefix + "hex:", v.Data as byte[] ?? []);
                break;
            default:
                AppendHex(sb, prefix + "hex(0):", v.Data as byte[] ?? []);
                break;
        }
    }

    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static byte[] StringBytes(string s) => Encoding.Unicode.GetBytes(s + "\0");

    static byte[] MultiBytes(string[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
            sb.Append(p).Append('\0');
        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    static void AppendHex(StringBuilder sb, string prefix, byte[] data)
    {
        sb.Append(prefix);
        var col = prefix.Length;
        for (var i = 0; i < data.Length; i++)
        {
            var last = i == data.Length - 1;
            var token = data[i].ToString("x2", CultureInfo.InvariantCulture) + (last ? "" : ",");
            if (!last && col + token.Length > LineWidth - 2)
            {
                sb.Append("\\\r\n  ");
                col = 2;
            }
            sb.Append(token);
            col += token.Length;
        }
        sb.Append("\r\n");
    }
}
