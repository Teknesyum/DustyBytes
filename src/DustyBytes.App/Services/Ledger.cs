using System.Text.Json;
using DustyBytes.Core;

namespace DustyBytes.App.Services;

public sealed class Ledger(string? path = null)
{
    readonly Lock _lock = new();

    public string Path { get; } = path ?? System.IO.Path.Combine(Paths.AppData, "ledger.json");

    public LedgerData Read()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(Path)
                    ? JsonSerializer.Deserialize(File.ReadAllText(Path), AppJson.Default.LedgerData) ?? new LedgerData(0, 0)
                    : new LedgerData(0, 0);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return new LedgerData(0, 0);
            }
        }
    }

    public const int HistoryMax = 500;

    public LedgerData Add(long freedBytes, DriveSpace? after = null, DateTimeOffset? at = null)
    {
        lock (_lock)
        {
            var current = Read();
            var bytes = Math.Max(0, freedBytes);
            var entry = new FreedEntry(at ?? DateTimeOffset.Now, bytes, after?.Root, after?.TotalBytes ?? 0, after?.FreeBytes ?? 0);
            var history = current.Entries.Append(entry).TakeLast(HistoryMax).ToList();
            var next = new LedgerData(current.FreedBytes + bytes, current.Actions + 1, history);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var temp = Path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(next, AppJson.Default.LedgerData));
                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            return next;
        }
    }
}
