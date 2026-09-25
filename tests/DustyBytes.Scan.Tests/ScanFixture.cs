using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DustyBytes.Scan.Tests;

public sealed partial class ScanFixture : IDisposable
{
    public string Root { get; }
    public string LongFile { get; }
    public string Denied { get; }
    public bool JunctionMade { get; }
    public bool HardLinkMade { get; }
    public bool Compressed { get; }
    private static readonly FileSystemAccessRule DenyRule = new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ListDirectory, AccessControlType.Deny);
    public static readonly DateTime Newest = new(2031, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public ScanFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "dustybytes-scan-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
        Write(@"a\file1.bin", 5000);
        Write(@"a\b\file2.bin", 10000);
        Write(@"a\b\c\file3.bin", 1);
        File.SetLastWriteTimeUtc(Path.Combine(Root, @"a\b\c\file3.bin"), Newest);
        Directory.CreateDirectory(Path.Combine(Root, "empty"));

        var deep = Root;
        for (var i = 0; i < 12; i++)
            deep = Path.Combine(deep, "uzun-klasor-adi-" + new string((char)('a' + i), 12));
        Directory.CreateDirectory(deep);
        LongFile = Path.Combine(deep, "derin.bin");
        File.WriteAllBytes(LongFile, new byte[3000]);

        Write("h1.bin", 8192);
        HardLinkMade = CreateHardLink(Path.Combine(Root, "h2.bin"), Path.Combine(Root, "h1.bin"), 0);

        JunctionMade = Run("cmd.exe", $"/c mklink /J \"{Path.Combine(Root, "j")}\" \"{Path.Combine(Root, "a")}\"") == 0;

        var comp = Path.Combine(Root, "comp.bin");
        File.WriteAllBytes(comp, new byte[1 << 20]);
        Compressed = Run("compact.exe", $"/c /q \"{comp}\"") == 0 && (File.GetAttributes(comp) & FileAttributes.Compressed) != 0;

        Denied = Path.Combine(Root, "denied");
        Directory.CreateDirectory(Denied);
        File.WriteAllBytes(Path.Combine(Denied, "gizli.bin"), new byte[100]);
        var acl = new DirectoryInfo(Denied).GetAccessControl();
        acl.AddAccessRule(DenyRule);
        new DirectoryInfo(Denied).SetAccessControl(acl);
    }

    private void Write(string rel, int size)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(p, data);
    }

    public static int Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public static bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static long Cluster
    {
        get
        {
            GetDiskFreeSpaceW(Path.GetPathRoot(Path.GetTempPath())!, out var spc, out var bps, out _, out _);
            return (long)spc * bps;
        }
    }

    public static long RoundUp(long v) => (v + Cluster - 1) / Cluster * Cluster;

    public void Dispose()
    {
        try
        {
            var acl = new DirectoryInfo(Denied).GetAccessControl();
            acl.RemoveAccessRule(DenyRule);
            new DirectoryInfo(Denied).SetAccessControl(acl);
        }
        catch (Exception)
        {
        }
        Run("cmd.exe", $"/c rmdir \"{Path.Combine(Root, "j")}\"");
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public IDisposable? LockDenied()
    {
        var h = CreateFileW(Denied, 0x80000000, 0, 0, 3, 0x02000000, 0);
        if (!h.IsInvalid)
            return h;
        h.Dispose();
        return null;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existing, nint sa);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string root, out uint spc, out uint bps, out uint free, out uint total);
}

[CollectionDefinition("scan")]
public sealed class ScanCollection : ICollectionFixture<ScanFixture>;
