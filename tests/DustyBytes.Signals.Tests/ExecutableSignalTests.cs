using System.Buffers.Binary;

namespace DustyBytes.Signals.Tests;

public class ExecutableSignalTests
{
    [Fact]
    public void Prefetch_RealFileParses()
    {
        var raw = File.ReadAllBytes(Fixture.Path("NOTEPAD.EXE-454B5413.pf"));
        Assert.True(PrefetchParser.IsCompressed(raw));
        var info = PrefetchParser.Parse(raw);
        Assert.True(info.Version is 30 or 31);
        Assert.Equal("NOTEPAD.EXE", info.ExeName);
        Assert.InRange(info.LastRuns.Count, 1, 8);
        Assert.True(info.RunCount >= 1);
        Assert.All(info.LastRuns, t => Assert.InRange(t.Year, 2015, 2100));
        Assert.True(info.LastRuns.SequenceEqual(info.LastRuns.OrderByDescending(t => t)));
        Assert.NotEmpty(info.Volumes);
        var device = info.ExeDevicePath();
        Assert.NotNull(device);
        Assert.EndsWith(@"\NOTEPAD.EXE", device, StringComparison.OrdinalIgnoreCase);
        var path = PrefetchParser.ToDrivePath(device!, info.Volumes, _ => @"C:\");
        Assert.NotNull(path);
        Assert.StartsWith(@"C:\", path);
        Assert.EndsWith(@"\NOTEPAD.EXE", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prefetch_SignalOnFixtureDir()
    {
        var dir = Fixture.TempDir();
        try
        {
            File.Copy(Fixture.Path("NOTEPAD.EXE-454B5413.pf"), Path.Combine(dir, "NOTEPAD.EXE-454B5413.pf"));
            File.WriteAllBytes(Path.Combine(dir, "BOZUK.EXE-00000000.pf"), [1, 2, 3]);
            var signal = new PrefetchSignal(dir, _ => @"C:\");
            var run = Assert.Single(signal.Read());
            Assert.Equal(1, signal.Failed);
            Assert.EndsWith(@"\NOTEPAD.EXE", run.ExePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Prefetch", run.Source);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Prefetch_RejectsGarbage()
    {
        Assert.ThrowsAny<Exception>(() => PrefetchParser.Parse(new byte[400]));
    }

    [Fact]
    public void Prefetch_UnknownSerialGivesNull()
    {
        var vols = new[] { new PrefetchVolume(@"\VOLUME{01d0-abcd}", 0xABCD) };
        Assert.Null(PrefetchParser.ToDrivePath(@"\VOLUME{01d0-abcd}\X\A.EXE", vols, _ => null));
        Assert.Equal(@"D:\X\A.EXE", PrefetchParser.ToDrivePath(@"\VOLUME{01d0-abcd}\X\A.EXE", vols, s => s == 0xABCD ? @"D:\" : null));
    }

    [Fact]
    public void Rot13_RoundTrip()
    {
        Assert.Equal(@"C:\Windows\notepad.exe", UserAssistParser.Rot13(@"P:\Jvaqbjf\abgrcnq.rkr"));
        Assert.Equal("{6D809377-6AF0-444B-8957-A3773F02200E}", UserAssistParser.Rot13("{6Q809377-6NS0-444O-8957-N3773S02200R}"));
    }

    [Fact]
    public void UserAssist_Win7Struct()
    {
        var data = new byte[72];
        var when = new DateTimeOffset(2025, 6, 1, 12, 30, 0, TimeSpan.Zero);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 90_000);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(60), when.ToFileTime());
        var e = UserAssistParser.ParseData(data)!;
        Assert.Equal(7, e.RunCount);
        Assert.Equal(3, e.FocusCount);
        Assert.Equal(TimeSpan.FromSeconds(90), e.FocusTime);
        Assert.Equal(when, e.LastRun);
    }

    [Fact]
    public void UserAssist_EmptyTimeIsNull()
    {
        var e = UserAssistParser.ParseData(new byte[72])!;
        Assert.Null(e.LastRun);
        Assert.Null(UserAssistParser.ParseData(new byte[8]));
    }

    [Fact]
    public void UserAssist_LegacyStruct()
    {
        var data = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 9);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero).ToFileTime());
        var e = UserAssistParser.ParseData(data)!;
        Assert.Equal(4, e.RunCount);
        Assert.Equal(2010, e.LastRun!.Value.Year);
    }

    [Fact]
    public void UserAssist_ResolvesKnownFolder()
    {
        var pf = new Guid("6D809377-6AF0-444B-8957-A3773F02200E");
        string? Folder(Guid g) => g == pf ? @"C:\Program Files" : null;
        var decoded = UserAssistParser.Rot13("{6Q809377-6NS0-444O-8957-N3773S02200R}\\Nqzva\\ncc.rkr");
        Assert.Equal(@"C:\Program Files\Admin\app.exe", UserAssistParser.ResolvePath(decoded, Folder));
        Assert.Equal(@"D:\x\y.exe", UserAssistParser.ResolvePath(@"D:\x\y.exe", Folder));
        Assert.Null(UserAssistParser.ResolvePath("Microsoft.Windows.Explorer", Folder));
        Assert.Null(UserAssistParser.ResolvePath("{00000000-0000-0000-0000-000000000001}\\a.exe", Folder));
    }

    [Fact]
    public void UserAssist_LiveReadDoesNotThrow()
    {
        var runs = new UserAssistSignal().Read();
        Assert.All(runs, r => Assert.EndsWith(".exe", r.ExePath, StringComparison.OrdinalIgnoreCase));
    }
}
