using System.Text.RegularExpressions;

namespace DustyBytes.Clean.Rules;

public static class Winapp2
{
    public static IReadOnlyList<CleanerRule> Parse(string iniText)
    {
        var rules = new List<CleanerRule>();
        CleanerRule? current = null;
        CleanerOption? option = null;

        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current is not null)
                    rules.Add(Explained(current));
                var section = line[1..^1];
                current = new CleanerRule
                {
                    Id = Slug(section),
                    Name = section,
                    Source = "winapp2.ini (MoscaDotTo/Winapp2, CC-BY-SA-4.0, veri, koda gömülmez)",
                };
                option = new CleanerOption { Id = "default", Label = section };
                current.Options.Add(option);
                continue;
            }

            if (current is null || option is null)
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (Regex.IsMatch(key, "^FileKey\\d+$", RegexOptions.IgnoreCase))
            {
                var parsed = ParseFileKey(value);
                if (parsed is not null)
                    option.Actions.Add(parsed);
            }
            else if (Regex.IsMatch(key, "^RegKey\\d+$", RegexOptions.IgnoreCase))
            {
                option.Actions.Add(new CleanAction
                {
                    Type = CleanActionType.RegistryDelete,
                    Key = ConvertRegistryRoot(value),
                });
            }
            else if (key.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                option.Warning = value;
            }
            else if (key.Equals("DetectFile", StringComparison.OrdinalIgnoreCase))
            {
                current.Running.LockFiles.Add(ConvertVariables(value));
            }
        }

        if (current is not null)
            rules.Add(Explained(current));

        return rules;
    }

    public const string GenericWhat = "Winapp2 topluluk listesinde önbellek ya da artık olarak işaretlenmiş dosyalar.";
    public const string GenericIfDeleted = "Uygulama bu dosyaları kaybeder; çoğu kendiliğinden yeniden oluşur, kişisel dosyalarına dokunulmaz.";
    public const string GenericReturns = "Çoğunlukla evet, uygulama gerektiğinde yeniden oluşturur; oluşturamazsa ayarı sıfırlanabilir.";
    public const string SessionIfDeleted = "Giriş yaptığın sitelerden ya da hesaplardan çıkış yapılabilir; yeniden giriş gerekir.";

    static CleanerRule Explained(CleanerRule rule)
    {
        foreach (var option in rule.Options)
        {
            option.What = GenericWhat;
            option.IfDeleted = GenericIfDeleted;
            option.Returns = GenericReturns;
            if (SessionData.Touches(option))
            {
                option.Sensitive = true;
                option.IfDeleted = SessionIfDeleted;
            }
        }
        return rule;
    }

    static CleanAction? ParseFileKey(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2)
            return null;
        var dir = ConvertVariables(parts[0]);
        var pattern = parts[1];
        var recurse = parts.Length > 2 && parts[2].Contains("RECURSE", StringComparison.OrdinalIgnoreCase);
        var path = pattern == "*" ? dir : $"{dir}\\{pattern}";
        return new CleanAction
        {
            Type = CleanActionType.Delete,
            Mode = pattern == "*" ? (recurse ? DeleteMode.Recurse : DeleteMode.FilesOnly) : DeleteMode.Glob,
            Path = path,
        };
    }

    static string ConvertVariables(string value) =>
        value
            .Replace("%AppData%", "%APPDATA%", StringComparison.OrdinalIgnoreCase)
            .Replace("%LocalAppData%", "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase)
            .Replace("%UserProfile%", "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace("%WinDir%", "%WINDIR%", StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramFiles%", "%PROGRAMFILES%", StringComparison.OrdinalIgnoreCase)
            .Replace('/', '\\');

    static string ConvertRegistryRoot(string value)
    {
        var converted = value
            .Replace("HKEY_CURRENT_USER", "HKCU", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_LOCAL_MACHINE", "HKLM", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCU\\", "HKCU\\", StringComparison.OrdinalIgnoreCase);
        var pipeIndex = converted.IndexOf('|');
        return pipeIndex >= 0 ? converted[..pipeIndex] : converted;
    }

    static string Slug(string section) =>
        Regex.Replace(section.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
