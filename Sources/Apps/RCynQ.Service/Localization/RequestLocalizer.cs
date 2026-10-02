using System.Globalization;
using RCynQ.Localization;

namespace RCynQ.Service.Localization;

/// <summary>
/// The localizer of the service: one object every service of the process is given, that words the answers of a request
/// in the language the client asked for and everything else in the language of the process.
/// </summary>
/// <remarks>
/// <para>
/// The services of the node are created once and serve every request, so they cannot be handed a localizer per request.
/// They are handed this one instead. A request that asks for a language enters <see cref="Use"/>; until the returned
/// scope is disposed, the texts asked for by that request, and by the work it starts, come in its language. Other
/// requests and the background work of the node are not affected.
/// </para>
/// <para>
/// The language of the process is the one the application started the service with; <see cref="SetLanguage"/> changes
/// it and raises <see cref="LanguageChanged"/>.
/// </para>
/// </remarks>
public sealed class RequestLocalizer : ILocalizer
{
    /// <summary>The languages the application has texts for.</summary>
    private readonly LanguageCatalog _catalog;

    /// <summary>The localizer of the process.</summary>
    private readonly Localizer _process;

    /// <summary>The localizer of the current request, or <see langword="null"/> outside a request that asked for a language.</summary>
    private readonly AsyncLocal<ILocalizer?> _request = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLocalizer"/> class.
    /// </summary>
    /// <param name="catalog">The languages the application has texts for.</param>
    /// <param name="code">The language of the process, or <see langword="null"/> (or a language without texts) for the default one.</param>
    public RequestLocalizer(LanguageCatalog catalog, string? code = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _process = new Localizer(catalog, code);
    }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged
    {
        add => _process.LanguageChanged += value;
        remove => _process.LanguageChanged -= value;
    }

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages => _process.Languages;

    /// <inheritdoc />
    public LanguageInfo Current => Active.Current;

    /// <inheritdoc />
    public CultureInfo Culture => Active.Culture;

    /// <summary>
    /// Gets the localizer in use: the one of the current request, otherwise the one of the process.
    /// </summary>
    private ILocalizer Active => _request.Value ?? _process;

    /// <summary>
    /// Makes the texts of the current flow of work, and of the work it starts, come in another language until the
    /// returned scope is disposed.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <returns>The scope to dispose to go back to the previous language.</returns>
    /// <exception cref="ArgumentException">There are no texts for the language.</exception>
    public IDisposable Use(string code)
    {
        if (_catalog.Find(code) is null)
        {
            throw new ArgumentException($"There are no texts for the language '{code}'.", nameof(code));
        }

        var previous = _request.Value;
        _request.Value = new Localizer(_catalog, code);
        return new Scope(_request, previous);
    }

    /// <summary>
    /// Chooses the first language, best quality first, that the application has texts for.
    /// </summary>
    /// <param name="requested">The languages a client asked for, with their quality.</param>
    /// <returns>The language code, or <see langword="null"/> when none is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requested"/> is <see langword="null"/>.</exception>
    public string? Choose(IEnumerable<(string? Language, double Quality)> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return requested
            .Where(item => item.Quality > 0 && !string.IsNullOrWhiteSpace(item.Language))
            .OrderByDescending(item => item.Quality)
            .Select(item => _catalog.Find(item.Language!.Split('-')[0]))
            .FirstOrDefault(pack => pack is not null)?.Language.Code;
    }

    /// <inheritdoc />
    public LanguageInfo Detect(CultureInfo culture) => _process.Detect(culture);

    /// <inheritdoc />
    public void SetLanguage(string code) => _process.SetLanguage(code);

    /// <inheritdoc />
    public string Get(string key) => Active.Get(key);

    /// <inheritdoc />
    public string Format(string key, params object?[] args) => Active.Format(key, args);

    /// <summary>
    /// Ends a <see cref="Use"/>.
    /// </summary>
    private sealed class Scope : IDisposable
    {
        /// <summary>The slot that holds the localizer of the flow.</summary>
        private readonly AsyncLocal<ILocalizer?> _slot;

        /// <summary>The localizer of the flow before the scope, or <see langword="null"/>.</summary>
        private readonly ILocalizer? _previous;

        /// <summary>
        /// Initializes a new instance of the <see cref="Scope"/> class.
        /// </summary>
        /// <param name="slot">The slot that holds the localizer of the flow.</param>
        /// <param name="previous">The localizer to go back to.</param>
        public Scope(AsyncLocal<ILocalizer?> slot, ILocalizer? previous)
        {
            _slot = slot;
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose() => _slot.Value = _previous;
    }
}
