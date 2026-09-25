using DustyBytes.Clean.Uninstall;

namespace DustyBytes.Uninstall.Tests;

public class RegistryExportTests
{
    static (FakeRegistryView Reg, RegKeyRef Key) Sample()
    {
        var reg = new FakeRegistryView();
        var key = reg.Key(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Acme\Widget");
        reg.Set(key, "", "default");
        reg.Set(key, "Path", @"C:\Acme ""Q""");
        reg.Set(key, "Exp", "%ProgramFiles%\\A", RegKind.ExpandString);
        reg.Set(key, "Multi", new[] { "ab", "c" });
        reg.Set(key, "Count", unchecked((int)0xDEADBEEF));
        reg.Set(key, "Big", 0x0102030405060708L);
        reg.Set(key, "Blob", new byte[] { 0x00, 0xFF, 0x10 });
        reg.Set(key.Child("Sub"), "X", 1);
        return (reg, key);
    }

    [Fact]
    public void HeaderAndKeyLines()
    {
        var (reg, key) = Sample();
        var text = RegistryExport.Render(reg, [new RegExportItem(key)]);

        Assert.StartsWith("Windows Registry Editor Version 5.00\r\n\r\n", text);
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Acme\\Widget]\r\n", text);
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Acme\\Widget\\Sub]\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public void ValueEncodings()
    {
        var (reg, key) = Sample();
        var text = RegistryExport.Render(reg, [new RegExportItem(key)]);

        Assert.Contains("@=\"default\"\r\n", text);
        Assert.Contains("\"Path\"=\"C:\\\\Acme \\\"Q\\\"\"\r\n", text);
        Assert.Contains("\"Exp\"=hex(2):25,00,50,00,72,00,6f,00", text);
        Assert.Contains("\"Multi\"=hex(7):61,00,62,00,00,00,63,00,00,00,00,00\r\n", text);
        Assert.Contains("\"Count\"=dword:deadbeef\r\n", text);
        Assert.Contains("\"Big\"=hex(b):08,07,06,05,04,03,02,01\r\n", text);
        Assert.Contains("\"Blob\"=hex:00,ff,10\r\n", text);
        Assert.Contains("\"X\"=dword:00000001\r\n", text);
        Assert.True(text.IndexOf("@=", StringComparison.Ordinal) < text.IndexOf("\"Path\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LongHexWrapsWithBackslashAndIndent()
    {
        var reg = new FakeRegistryView();
        var key = reg.Key(RegHive.CurrentUser, RegView.Registry64, @"Software\Acme");
        reg.Set(key, "Long", Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());

        var text = RegistryExport.Render(reg, [new RegExportItem(key)]);
        var lines = text.Split("\r\n");
        var start = Array.FindIndex(lines, l => l.StartsWith("\"Long\"=hex:", StringComparison.Ordinal));

        Assert.True(start > 0);
        Assert.EndsWith("\\", lines[start]);
        Assert.StartsWith("  ", lines[start + 1]);
        var i = start;
        while (lines[i].EndsWith('\\'))
        {
            Assert.True(lines[i].Length <= 80, lines[i]);
            i++;
        }
        var joined = string.Concat(lines[start..(i + 1)].Select(l => l.TrimEnd('\\').Trim()));
        Assert.Equal("\"Long\"=hex:" + string.Join(",", Enumerable.Range(0, 100).Select(b => b.ToString("x2"))), joined);
        Assert.Contains("[HKEY_CURRENT_USER\\Software\\Acme]", text);
    }

    [Fact]
    public void StringWithNewlineFallsBackToHex1()
    {
        var reg = new FakeRegistryView();
        var key = reg.Key(RegHive.CurrentUser, RegView.Registry64, @"Software\Acme");
        reg.Set(key, "Note", "a\nb");

        Assert.Contains("\"Note\"=hex(1):61,00,0a,00,62,00,00,00", RegistryExport.Render(reg, [new RegExportItem(key)]));
    }

    [Fact]
    public void Wow64KeyUsesPhysicalPath()
    {
        var reg = new FakeRegistryView();
        var key = reg.Key(RegHive.LocalMachine, RegView.Registry32, @"SOFTWARE\Acme");
        reg.Set(key, "A", "1");

        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Acme]", RegistryExport.Render(reg, [new RegExportItem(key)]));
    }

    [Fact]
    public void SingleValueExportContainsOnlyThatValue()
    {
        var (reg, key) = Sample();
        var text = RegistryExport.Render(reg, [new RegExportItem(key, "Count")]);

        Assert.Contains("\"Count\"=dword:deadbeef", text);
        Assert.DoesNotContain("\"Path\"", text);
        Assert.DoesNotContain("Sub]", text);
    }

    [Fact]
    public void FileIsUtf16LittleEndianWithBom()
    {
        var (reg, key) = Sample();
        using var t = new TempTree();
        var file = Path.Combine(t.Root, "out", "backup.reg");

        RegistryExport.ToRegFile(reg, [key], file);

        var bytes = File.ReadAllBytes(file);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.Equal((byte)'W', bytes[2]);
        Assert.Equal(0, bytes[3]);
        Assert.StartsWith(RegistryExport.Header, System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
    }
}
