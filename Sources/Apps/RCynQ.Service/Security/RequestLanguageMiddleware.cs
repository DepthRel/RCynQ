using Microsoft.AspNetCore.Http;
using RCynQ.Service.Localization;

namespace RCynQ.Service.Security;

/// <summary>
/// Words the answers of one request (errors, diagnostics, results) in the language the client asked for with the
/// <c>Accept-Language</c> header. A request without a language the application has texts for is answered in the
/// language of the process.
/// </summary>
public sealed class RequestLanguageMiddleware
{
    /// <summary>
    /// The next middleware.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// The localizer of the service.
    /// </summary>
    private readonly RequestLocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLanguageMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="localizer">The localizer of the service.</param>
    public RequestLanguageMiddleware(RequestDelegate next, RequestLocalizer localizer)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Runs the rest of the pipeline with the texts of the requested language.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the request.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var requested = _localizer.Choose(context.Request.GetTypedHeaders().AcceptLanguage.Select(value => (value.Value.Value, value.Quality ?? 1.0)));
        if (requested is null)
        {
            await _next(context);
            return;
        }

        using (_localizer.Use(requested))
        {
            await _next(context);
        }
    }
}
