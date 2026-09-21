using Microsoft.AspNetCore.Http;
using RCynQ.Localization;

namespace RCynQ.Service.Security;

/// <summary>
/// Words the answers of one request (errors, diagnostics, results) in the language the client asked for with the
/// <c>Accept-Language</c> header. A request without a language the application has texts for is answered in the
/// language of the process.
/// </summary>
public sealed class RequestLanguageMiddleware
{
    /// <summary>
    /// The languages the application has texts for.
    /// </summary>
    private static readonly LanguageCatalog Catalog = LanguageCatalog.LoadEmbedded();

    /// <summary>
    /// The next middleware.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLanguageMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    public RequestLanguageMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Runs the rest of the pipeline with the texts of the requested language.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the request.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var requested = Choose(context.Request.GetTypedHeaders().AcceptLanguage.Select(value => (value.Value.Value, value.Quality ?? 1.0)));
        if (requested is null)
        {
            await _next(context);
            return;
        }

        using (Texts.Scope(new Localizer(Catalog, requested)))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Chooses the first language, best quality first, that the application has texts for.
    /// </summary>
    /// <param name="requested">The languages of the header with their quality.</param>
    /// <returns>The language code, or <see langword="null"/> when none is available.</returns>
    public static string? Choose(IEnumerable<(string? Language, double Quality)> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return requested
            .Where(item => item.Quality > 0 && !string.IsNullOrWhiteSpace(item.Language))
            .OrderByDescending(item => item.Quality)
            .Select(item => Catalog.Find(item.Language!.Split('-')[0]))
            .FirstOrDefault(pack => pack is not null)?.Language.Code;
    }
}
