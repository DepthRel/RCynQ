using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// <see cref="ILocalEndpointProbe"/> using HTTP with the system proxy disabled.
/// </summary>
public sealed class HttpLocalEndpointProbe : ILocalEndpointProbe, IDisposable
{
    /// <summary>The HTTP client.</summary>
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"http://127.0.0.1:{port}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
