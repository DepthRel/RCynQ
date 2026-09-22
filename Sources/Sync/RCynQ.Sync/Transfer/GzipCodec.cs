using System.IO.Compression;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Compresses and decompresses the bodies of patch requests with gzip.
/// </summary>
/// <remarks>
/// Both the manifest and the content of every file travel gzip-compressed. Decompression always has an upper bound, so a
/// small request that expands enormously cannot exhaust the memory or the disk of the receiver.
/// </remarks>
public static class GzipCodec
{
    /// <summary>
    /// The value of the <c>Content-Encoding</c> header of compressed bodies.
    /// </summary>
    public const string ContentEncoding = "gzip";

    /// <summary>
    /// Compresses a buffer.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The gzip stream bytes.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Wraps a request body in a decompressing stream.
    /// </summary>
    /// <param name="compressed">The compressed body.</param>
    /// <returns>A stream of the uncompressed bytes.</returns>
    public static Stream Decompress(Stream compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        return new GZipStream(compressed, CompressionMode.Decompress);
    }

    /// <summary>
    /// Reads a compressed body completely into memory, refusing bodies that expand beyond a limit.
    /// </summary>
    /// <param name="compressed">The compressed body.</param>
    /// <param name="maxBytes">The largest uncompressed size accepted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The uncompressed bytes.</returns>
    /// <exception cref="InvalidDataException">The body is not valid gzip data or expands beyond <paramref name="maxBytes"/>.</exception>
    public static async Task<byte[]> DecompressAsync(Stream compressed, long maxBytes, CancellationToken cancellationToken = default)
    {
        await using var gzip = Decompress(compressed);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await gzip.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                throw new InvalidDataException($"The uncompressed body exceeds {maxBytes} bytes.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
