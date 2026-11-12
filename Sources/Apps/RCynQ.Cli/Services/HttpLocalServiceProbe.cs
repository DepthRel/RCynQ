using System.Globalization;
using RCynQ.Core.Api;

namespace RCynQ.Cli.Services;

/// <summary>
/// <see cref="ILocalServiceProbe"/> that asks <see cref="ApiRoutes.HEALTH"/> of the local API, which needs no token.
/// </summary>
public sealed class HttpLocalServiceProbe : ILocalServiceProbe
{
    /// <summary>
    /// How long the probe waits for an answer: the service answers on loopback at once.
    /// </summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = _timeout };
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{ApiRoutes.HEALTH}"));
        try
        {
            // Any answer means the port is taken by a running service; only a refused connection means it is free.
            using var response = await client.GetAsync(uri, cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
