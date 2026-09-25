using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace DustyBytes.Signals;

public sealed record PrefetchVolume(string DevicePath, uint Serial);

public sealed record PrefetchInfo(
    int Version,
    string ExeName,
    IReadOnlyList<DateTimeOffset> LastRuns,
    int RunCount,
    IReadOnlyList<string> Files,
    IReadOnlyList<PrefetchVolume> Volumes)
{
    public string? ExeDevicePath()
    {
        var name = ExeName;
        string? prefix = null;
        foreach (var f in Files)
        {
            var file = Path.GetFileName(f);
            if (file.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
            if (prefix is null && name.Length >= 29 && file.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && file.EndsWith(".EXE", StringComparison.OrdinalIgnoreCase))
                prefix = f;
        }
        return prefix;
    }
}

public static partial class PrefetchParser
{
    private const int Scca = 0x41434353;
    private const ushort CompressionFormatXpressHuff = 4;

    public static PrefetchInfo Parse(byte[] raw)
    {
        var data = IsCompressed(raw) ? Decompress(raw) : raw;
        return ParseDecompressed(data);
    }

    public static bool IsCompressed(ReadOnlySpan<byte> raw) =>
        raw.Length > 8 && raw[0] == (byte)'M' && raw[1] == (byte)'A' && raw[2] == (byte)'M';

    public static PrefetchInfo ParseDecompressed(byte[] d)
    {
        if (d.Length < 308)
            throw new InvalidDataException("Prefetch dosyası çok kısa");
        var version = I32(d, 0);
        if (I32(d, 4) != Scca)
            throw new InvalidDataException("SCCA imzası yok");
        if (version is not (30 or 31))
            throw new NotSupportedException($"Prefetch sürümü {version} desteklenmiyor");

        var exeName = Encoding.Unicode.GetString(d, 16, 60);
        var nul = exeName.IndexOf('\0');
        if (nul >= 0)
            exeName = exeName[..nul];

        const int fi = 84;
        var filenamesOffset = I32(d, fi + 16);
        var filenamesSize = I32(d, fi + 20);
        var volumesOffset = I32(d, fi + 24);
        var volumeCount = I32(d, fi + 28);

        var runs = new List<DateTimeOffset>(8);
        for (var i = 0; i < 8; i++)
        {
            var ft = BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(fi + 44 + i * 8));
            if (ft > 0)
            {
                try
                {
                    runs.Add(DateTimeOffset.FromFileTime(ft).ToUniversalTime());
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }
        runs.Sort((a, b) => b.CompareTo(a));

        var runCount = I32(d, fi + 124);
        if (I32(d, fi + 120) != 0)
            runCount = I32(d, fi + 116);

        var files = new List<string>();
        if (filenamesOffset > 0 && filenamesSize > 0 && filenamesOffset + filenamesSize <= d.Length)
        {
            var text = Encoding.Unicode.GetString(d, filenamesOffset, filenamesSize);
            files.AddRange(text.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        var volumes = new List<PrefetchVolume>();
        for (var j = 0; j < volumeCount && j < 64; j++)
        {
            var v = volumesOffset + j * 96;
            if (v + 96 > d.Length)
                break;
            var devOffset = I32(d, v);
            var devChars = I32(d, v + 4);
            var serial = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(v + 16));
            var start = volumesOffset + devOffset;
            if (devChars <= 0 || start + devChars * 2 > d.Length)
                continue;
            volumes.Add(new PrefetchVolume(Encoding.Unicode.GetString(d, start, devChars * 2), serial));
        }

        return new PrefetchInfo(version, exeName, runs, runCount, files, volumes);
    }

    public static string? ToDrivePath(string devicePath, IReadOnlyList<PrefetchVolume> volumes, Func<uint, string?> driveForSerial)
    {
        foreach (var vol in volumes)
        {
            if (!devicePath.StartsWith(vol.DevicePath, StringComparison.OrdinalIgnoreCase))
                continue;
            var drive = driveForSerial(vol.Serial);
            if (drive is null)
                return null;
            var rest = devicePath[vol.DevicePath.Length..].TrimStart('\\');
            return Path.Combine(drive, rest);
        }
        return null;
    }

    public static unsafe byte[] Decompress(byte[] raw)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4));
        if (size == 0 || size > 64 * 1024 * 1024)
            throw new InvalidDataException("MAM boyutu geçersiz");
        var dataOffset = (raw[3] & 0x80) != 0 ? 12 : 8;
        var output = new byte[size];
        var status = RtlGetCompressionWorkSpaceSize(CompressionFormatXpressHuff, out var workSize, out _);
        if (status != 0)
            throw new InvalidOperationException($"RtlGetCompressionWorkSpaceSize 0x{status:X8}");
        var work = NativeMemory.Alloc(workSize);
        try
        {
            fixed (byte* src = &raw[dataOffset])
            fixed (byte* dst = output)
            {
                status = RtlDecompressBufferEx(CompressionFormatXpressHuff, dst, size, src, (uint)(raw.Length - dataOffset), out var final, work);
                if (status != 0)
                    throw new InvalidDataException($"RtlDecompressBufferEx 0x{status:X8}");
                if (final != size)
                    Array.Resize(ref output, (int)final);
            }
        }
        finally
        {
            NativeMemory.Free(work);
        }
        return output;
    }

    private static int I32(byte[] d, int offset) =>
        offset + 4 <= d.Length ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(offset)) : 0;

    [LibraryImport("ntdll.dll")]
    private static partial int RtlGetCompressionWorkSpaceSize(ushort format, out uint bufferWorkSpaceSize, out uint fragmentWorkSpaceSize);

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int RtlDecompressBufferEx(ushort format, byte* uncompressed, uint uncompressedSize, byte* compressed, uint compressedSize, out uint finalUncompressedSize, void* workSpace);
}

public sealed partial class PrefetchSignal : IExecutableSignal
{
    private readonly string _dir;
    private readonly Func<uint, string?> _driveForSerial;

    public PrefetchSignal(string? dir = null, Func<uint, string?>? driveForSerial = null)
    {
        _dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        _driveForSerial = driveForSerial ?? VolumeSerials.Current().DriveFor;
    }

    public string Source => SourceNames.Prefetch;
    public double Reliability => Signals.Reliability.Prefetch;

    public int Failed { get; private set; }

    public IReadOnlyList<ExecutableRun> Read()
    {
        Failed = 0;
        var runs = new Dictionary<string, ExecutableRun>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_dir))
            return [];
        foreach (var file in Directory.EnumerateFiles(_dir, "*.pf"))
        {
            try
            {
                var info = PrefetchParser.Parse(File.ReadAllBytes(file));
                if (info.LastRuns.Count == 0)
                    continue;
                var device = info.ExeDevicePath();
                var path = device is null ? null : PrefetchParser.ToDrivePath(device, info.Volumes, _driveForSerial);
                if (path is null)
                    continue;
                var run = new ExecutableRun(path, info.LastRuns[0], info.RunCount, Source);
                if (!runs.TryGetValue(path, out var existing))
                    runs[path] = run;
                else
                    runs[path] = existing with
                    {
                        LastRun = existing.LastRun > run.LastRun ? existing.LastRun : run.LastRun,
                        RunCount = existing.RunCount + run.RunCount,
                    };
            }
            catch
            {
                Failed++;
            }
        }
        return runs.Values.ToList();
    }
}

public sealed partial class VolumeSerials
{
    private readonly Dictionary<uint, string> _map;

    private VolumeSerials(Dictionary<uint, string> map) => _map = map;

    public static VolumeSerials Current()
    {
        var map = new Dictionary<uint, string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory || !drive.IsReady)
                    continue;
                if (GetVolumeInformationW(drive.RootDirectory.FullName, null, 0, out var serial, out _, out _, null, 0))
                    map.TryAdd(serial, drive.RootDirectory.FullName);
            }
            catch
            {
            }
        }
        return new VolumeSerials(map);
    }

    public string? DriveFor(uint serial) => _map.TryGetValue(serial, out var d) ? d : null;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationW(string rootPathName, char[]? volumeNameBuffer, int volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, char[]? fileSystemNameBuffer, int fileSystemNameSize);
}
