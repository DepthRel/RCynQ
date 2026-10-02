using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// An HTTP body that compresses a stream with gzip while it is being sent.
/// </summary>
/// <remarks>
/// The source is read and compressed in small blocks and written straight to the network, so a large file is never held
/// in memory. The length is not known in advance, so the body is sent with chunked transfer encoding.
/// </remarks>
public sealed class GzipStreamContent : HttpContent
{
    /// <summary>
    /// The uncompressed source.
    /// </summary>
    private readonly Stream _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="GzipStreamContent"/> class.
    /// </summary>
    /// <param name="source">The uncompressed source; it is disposed together with the content.</param>
    public GzipStreamContent(Stream source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        Headers.ContentEncoding.Add(GzipCodec.CONTENT_ENCODING);
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
        await _source.CopyToAsync(gzip, cancellationToken);
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }
}
