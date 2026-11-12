using System.Text;
using RCynQ.UI.Services.About;

namespace RCynQ.UI.Services.Updates;

/// <summary>
/// <see cref="IUpdateFeed"/> that reads <see cref="ProjectSite.UPDATES_URL"/>. The request carries nothing about the user
/// or the nodes: a plain GET without cookies, with a name of the program that has no version in it.
/// </summary>
public sealed class HttpUpdateFeed : IUpdateFeed, IDisposable
{
    /// <summary>
    /// The largest answer that is read; the real list is a few hundred bytes.
    /// </summary>
    public const int MAX_BYTES = 64 * 1024;

    /// <summary>
    /// How long the page has to answer.
    /// </summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The address of the list.
    /// </summary>
    private readonly Uri _address;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpUpdateFeed"/> class.
    /// </summary>
    /// <param name="handler">The HTTP handler; <see langword="null"/> uses the default one with the proxy of the system.</param>
    /// <param name="address">The address of the list; <see langword="null"/> uses <see cref="ProjectSite.UPDATES_URL"/>.</param>
    public HttpUpdateFeed(HttpMessageHandler? handler = null, Uri? address = null)
    {
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { UseCookies = false })
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = _timeout;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RCynQ");
        _address = address ?? new Uri(ProjectSite.UPDATES_URL);
    }

    /// <inheritdoc />
    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(_address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MAX_BYTES)
            {
                throw new InvalidDataException($"The list of releases is larger than {MAX_BYTES} bytes.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ReadLimitedAsync(stream, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The project page did not answer in time.", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Reads a stream as UTF-8 text, refusing it once it grows larger than <see cref="MAX_BYTES"/>, whatever its headers said.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidDataException">The stream is too large.</exception>
    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MAX_BYTES + 1];
        var length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
            if (length > MAX_BYTES)
            {
                throw new InvalidDataException($"The list of releases is larger than {MAX_BYTES} bytes.");
            }
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }
}
