using System.Text;

namespace DustyBytes.Signals;

public sealed class VdfNode
{
    public string Name { get; }
    public string? Value { get; }
    public List<VdfNode> Children { get; } = [];

    public VdfNode(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    public VdfNode? this[string key] =>
        Children.FirstOrDefault(c => c.Name.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string? Get(string key) => this[key]?.Value;

    public long? GetLong(string key) =>
        long.TryParse(Get(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}

public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text);
        var pos = 0;
        var root = new VdfNode("");
        ReadChildren(tokens, ref pos, root, topLevel: true);
        return root.Children.Count == 1 && root.Children[0].Value is null ? root.Children[0] : root;
    }

    public static VdfNode ParseFile(string path) => Parse(File.ReadAllText(path));

    private static void ReadChildren(List<(string Text, bool Quoted)> tokens, ref int pos, VdfNode parent, bool topLevel)
    {
        while (pos < tokens.Count)
        {
            var (text, quoted) = tokens[pos];
            if (!quoted && text == "}")
            {
                pos++;
                if (!topLevel)
                    return;
                continue;
            }
            if (!quoted && text == "{")
            {
                pos++;
                continue;
            }
            pos++;
            if (pos >= tokens.Count)
            {
                parent.Children.Add(new VdfNode(text, ""));
                return;
            }
            var next = tokens[pos];
            if (!next.Quoted && next.Text == "{")
            {
                pos++;
                var node = new VdfNode(text);
                ReadChildren(tokens, ref pos, node, topLevel: false);
                parent.Children.Add(node);
            }
            else if (!next.Quoted && next.Text == "}")
            {
                parent.Children.Add(new VdfNode(text, ""));
            }
            else
            {
                pos++;
                parent.Children.Add(new VdfNode(text, next.Text));
            }
        }
    }

    private static List<(string Text, bool Quoted)> Tokenize(string s)
    {
        var list = new List<(string, bool)>();
        var i = 0;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n')
                    i++;
                continue;
            }
            if (c == '[')
            {
                while (i < s.Length && s[i] != ']')
                    i++;
                i++;
                continue;
            }
            if (c is '{' or '}')
            {
                list.Add((c.ToString(), false));
                i++;
                continue;
            }
            sb.Clear();
            if (c == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        var e = s[i + 1];
                        sb.Append(e switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => e });
                        if (e is not ('n' or 't' or '"' or '\\'))
                            sb.Insert(sb.Length - 1, '\\');
                        i += 2;
                        continue;
                    }
                    sb.Append(s[i++]);
                }
                i++;
                list.Add((sb.ToString(), true));
                continue;
            }
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '"'))
                sb.Append(s[i++]);
            list.Add((sb.ToString(), false));
        }
        return list;
    }
}
