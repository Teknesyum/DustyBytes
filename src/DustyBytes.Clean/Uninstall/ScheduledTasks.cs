using System.Xml;

namespace DustyBytes.Clean.Uninstall;

public sealed record TaskEntry(string TaskPath, string File, IReadOnlyList<string> Commands);

public static class ScheduledTasks
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");

    public static IEnumerable<TaskEntry> Enumerate(string tasksDir)
    {
        if (!Directory.Exists(tasksDir))
            yield break;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(tasksDir, "*", options).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var file in files)
        {
            var commands = ReadCommands(file);
            if (commands.Count == 0)
                continue;
            var rel = Path.GetRelativePath(tasksDir, file);
            yield return new TaskEntry("\\" + rel, file, commands);
        }
    }

    public static IReadOnlyList<string> ReadCommands(string file)
    {
        var list = new List<string>();
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 1024 * 1024)
                return list;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true });
            string? command = null;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Command")
                {
                    command = reader.ReadElementContentAsString().Trim();
                    if (command.Length > 0)
                        list.Add(command);
                    continue;
                }
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Arguments" && command is not null)
                {
                    var args = reader.ReadElementContentAsString().Trim();
                    if (args.Length > 0)
                        list.Add(args);
                    continue;
                }
                reader.Read();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
        }
        return list;
    }
}
