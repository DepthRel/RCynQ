using Microsoft.AspNetCore.Http;
using RCynQ.Core.Api;
using RCynQ.Localization;

namespace RCynQ.Service.Security;

/// <summary>
/// Requires the local API token on every request except <c>GET /health</c>, the patch session routes (<c>/patch/sessions/...</c>) and <c>POST /git/bundle</c>.
/// </summary>
/// <remarks>
/// The token is read from the <c>X-Api-Token</c> header; SignalR clients may pass it as the <c>access_token</c> query
/// parameter on the hub route because browsers and some transports cannot set headers.
/// </remarks>
public sealed class ApiTokenMiddleware
{
    /// <summary>The next middleware.</summary>
    private readonly RequestDelegate _next;

    /// <summary>The token store.</summary>
    private readonly ApiTokenStore _tokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiTokenMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="tokens">The token store.</param>
    public ApiTokenMiddleware(RequestDelegate next, ApiTokenStore tokens)
    {
        _next = next;
        _tokens = tokens;
    }

    /// <summary>
    /// Checks the token and continues or answers <c>401</c>.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the request.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isPublic = path.Equals(ApiRoutes.Health, StringComparison.OrdinalIgnoreCase)
            || (path.Equals(ApiRoutes.GitBundle, StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(context.Request.Method))
            || (path.StartsWithSegments(ApiRoutes.PatchSessionPrefix, StringComparison.OrdinalIgnoreCase)
                && (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)));
        if (isPublic)
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers[ApiHeaders.ApiToken].FirstOrDefault();
        if (token is null && path.StartsWithSegments(ApiRoutes.Hub, StringComparison.OrdinalIgnoreCase))
        {
            token = context.Request.Query[ApiHeaders.AccessTokenQuery].FirstOrDefault();
        }

        if (!_tokens.IsValid(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(Texts.Get("Api.TokenInvalid")));
            return;
        }

        await _next(context);
    }
}
