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

    public LedgerData Add(long freedBytes)
    {
        lock (_lock)
        {
            var current = Read();
            var next = new LedgerData(current.FreedBytes + Math.Max(0, freedBytes), current.Actions + 1);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(next, AppJson.Default.LedgerData));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            return next;
        }
    }
}
