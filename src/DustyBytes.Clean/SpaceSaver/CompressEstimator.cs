using System.IO.Compression;
using DustyBytes.Core;

namespace DustyBytes.Clean.SpaceSaver;

public sealed record CompressionEstimate(long EligibleBytes, long GainBytes, int Files, int Sampled);

public static class CompressEstimator
{
    public const int ChunkBytes = 8192;
    public const int ClusterBytes = 4096;
    public const int ChunksPerFile = 4;
    public const int MaxSamples = 48;
    public const double Margin = 0.8;

    public static CompressionEstimate Estimate(IReadOnlyList<PlannedFile> files, CancellationToken ct = default) =>
        Estimate(files, ReadChunks, ct);

    public static CompressionEstimate Estimate(IReadOnlyList<PlannedFile> files, Func<PlannedFile, IEnumerable<byte[]>> chunks, CancellationToken ct = default)
    {
        var eligible = files.Sum(f => f.Length);
        if (files.Count == 0 || eligible <= 0)
            return new CompressionEstimate(0, 0, 0, 0);

        double weighted = 0;
        long weight = 0;
        var sampled = 0;
        foreach (var file in Sample(files))
        {
            ct.ThrowIfCancellationRequested();
            long read = 0, saved = 0;
            foreach (var chunk in chunks(file))
            {
                read += chunk.Length;
                saved += SavedBytes(chunk);
            }
            if (read == 0)
                continue;
            sampled++;
            weighted += (double)saved / read * file.Length;
            weight += file.Length;
        }

        if (weight == 0)
            return new CompressionEstimate(eligible, 0, files.Count, 0);
        var gain = (long)(weighted / weight * eligible * Margin);
        return new CompressionEstimate(eligible, Math.Max(0, gain), files.Count, sampled);
    }

    public static IReadOnlyList<PlannedFile> Sample(IReadOnlyList<PlannedFile> files)
    {
        if (files.Count <= MaxSamples)
            return files;
        var ordered = files.OrderByDescending(f => f.Length).ToList();
        var top = MaxSamples / 3;
        var picked = ordered.Take(top).ToList();
        var rest = ordered.Skip(top).ToList();
        var need = MaxSamples - top;
        for (var i = 0; i < need; i++)
            picked.Add(rest[(int)((long)i * rest.Count / need)]);
        return picked;
    }

    public static long SavedBytes(byte[] chunk)
    {
        if (chunk.Length == 0)
            return 0;
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(chunk);
        var allocated = RoundUp(Math.Min(buffer.Length, chunk.Length));
        return Math.Max(0, RoundUp(chunk.Length) - allocated);
    }

    static long RoundUp(long bytes) => (bytes + ClusterBytes - 1) / ClusterBytes * ClusterBytes;

    static IEnumerable<byte[]> ReadChunks(PlannedFile file)
    {
        var result = new List<byte[]>();
        try
        {
            using var stream = new FileStream(Paths.ToLong(file.Path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
            var length = stream.Length;
            for (var i = 0; i < ChunksPerFile; i++)
            {
                var offset = length <= ChunkBytes ? 0 : length * i / ChunksPerFile / ChunkBytes * ChunkBytes;
                stream.Position = offset;
                var chunk = new byte[(int)Math.Min(ChunkBytes, length - offset)];
                var got = stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false);
                if (got > 0)
                    result.Add(got == chunk.Length ? chunk : chunk[..got]);
                if (length <= ChunkBytes)
                    break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return result;
    }
}
