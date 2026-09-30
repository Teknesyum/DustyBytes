using System.Buffers;
using System.Security.Cryptography;

namespace DustyBytes.Scan.Duplicates;

public static class ContentHash
{
    public const int EdgeBytes = 4096;
    const int Chunk = 1 << 20;

    public static string Edge(FileStream stream, long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[EdgeBytes];
        stream.Position = 0;
        var head = Fill(stream, buffer, (int)Math.Min(EdgeBytes, length));
        hash.AppendData(buffer, 0, head);
        if (length > EdgeBytes)
        {
            stream.Position = Math.Max(EdgeBytes, length - EdgeBytes);
            var tail = Fill(stream, buffer, EdgeBytes);
            hash.AppendData(buffer, 0, tail);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string Full(FileStream stream, CancellationToken ct, Action<long>? read = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            stream.Position = 0;
            int n;
            while ((n = stream.Read(buffer, 0, Chunk)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, n);
                read?.Invoke(n);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static int Fill(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = stream.Read(buffer, total, count - total);
            if (n == 0)
                break;
            total += n;
        }
        return total;
    }
}
