using System.Buffers.Binary;
using System.Text;

namespace DustyBytes.Clean.Uninstall;

public static class ShellLink
{
    const uint HasLinkTargetIdList = 0x1;
    const uint HasLinkInfo = 0x2;
    const uint HasName = 0x4;
    const uint HasRelativePath = 0x8;
    const uint HasWorkingDir = 0x10;
    const uint HasArguments = 0x20;
    const uint HasIconLocation = 0x40;
    const uint IsUnicode = 0x80;
    const uint EnvironmentBlockSignature = 0xA0000001;

    public static string? ReadTarget(string lnkFile)
    {
        try
        {
            var info = new FileInfo(lnkFile);
            if (!info.Exists || info.Length > 1024 * 1024)
                return null;
            return ParseTarget(File.ReadAllBytes(lnkFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? ParseTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 76 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x4C)
            return null;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        var pos = 76;
        try
        {
            if ((flags & HasLinkTargetIdList) != 0)
                pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);

            string? target = null;
            if ((flags & HasLinkInfo) != 0)
            {
                var info = data[pos..];
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(info);
                var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
                var infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
                var localBase = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[16..]);
                var suffix = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[24..]);
                if ((infoFlags & 1) != 0)
                {
                    if (headerSize >= 0x24)
                    {
                        var localBaseU = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[28..]);
                        var suffixU = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[32..]);
                        target = ReadUnicodeZ(info, localBaseU) + (suffixU > 0 ? ReadUnicodeZ(info, suffixU) : "");
                    }
                    else
                    {
                        target = ReadAnsiZ(info, localBase) + (suffix > 0 ? ReadAnsiZ(info, suffix) : "");
                    }
                }
                pos += size;
            }

            var unicode = (flags & IsUnicode) != 0;
            foreach (var f in new[] { HasName, HasRelativePath, HasWorkingDir, HasArguments, HasIconLocation })
            {
                if ((flags & f) == 0)
                    continue;
                var count = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
                pos += 2 + count * (unicode ? 2 : 1);
            }

            while (pos + 8 <= data.Length)
            {
                var blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
                if (blockSize < 4)
                    break;
                var sig = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
                if (sig == EnvironmentBlockSignature && blockSize >= 0x314 && string.IsNullOrEmpty(target))
                {
                    var uni = data.Slice(pos + 8 + 260, 520);
                    var s = ReadUnicodeZ(uni, 0);
                    if (string.IsNullOrEmpty(s))
                        s = ReadAnsiZ(data.Slice(pos + 8, 260), 0);
                    target = Environment.ExpandEnvironmentVariables(s);
                }
                pos += blockSize;
            }
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    static string ReadAnsiZ(ReadOnlySpan<byte> span, int offset)
    {
        var s = span[offset..];
        var end = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? s : s[..end]);
    }

    static string ReadUnicodeZ(ReadOnlySpan<byte> span, int offset)
    {
        var s = span[offset..];
        var len = 0;
        while (len + 1 < s.Length && (s[len] != 0 || s[len + 1] != 0))
            len += 2;
        return Encoding.Unicode.GetString(s[..len]);
    }
}
