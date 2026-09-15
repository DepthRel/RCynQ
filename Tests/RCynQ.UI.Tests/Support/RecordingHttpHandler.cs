using System.Net;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// Records requests and answers them with a configurable status code.
/// </summary>
public sealed class RecordingHttpHandler : HttpMessageHandler
{
    /// <summary>Gets the recorded API token header values (<see langword="null"/> when absent), in request order.</summary>
    public List<string?> Tokens { get; } = new();

    /// <summary>Gets or sets the status code of the next responses.</summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Tokens.Add(request.Headers.TryGetValues(Core.Api.ApiHeaders.ApiToken, out var values) ? values.Single() : null);
        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = new StringContent("{}") });
    }
}
