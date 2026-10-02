using System.Net;
using RCynQ.Core.Api;
using RCynQ.Localization;
using RCynQ.UI.Services.Connection;

namespace RCynQ.UI.Services.Api;

/// <summary>
/// Adds the current local API token to every request and invalidates it when the service rejects it.
/// </summary>
public sealed class ApiTokenHandler : DelegatingHandler
{
    /// <summary>The token provider.</summary>
    private readonly ApiTokenProvider _tokens;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiTokenHandler"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="tokens">The token provider.</param>
    /// <param name="innerHandler">The inner handler.</param>
    public ApiTokenHandler(ILocalizer localizer, ApiTokenProvider tokens, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ApiHeaders.ApiToken);
        var token = _tokens.GetToken();
        if (token.Length > 0)
        {
            // Add validates the value, so a tampered token file cannot inject header lines.
            request.Headers.Add(ApiHeaders.ApiToken, token);
        }

        // The service words its answers (errors, diagnostics) in the language the user chose.
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.ParseAdd(_localizer.Current.Code);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokens.Invalidate();
        }

        return response;
    }
}
