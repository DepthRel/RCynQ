using System.Globalization;
using RCynQ.Localization;
using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Localization;

/// <summary>
/// Keeps the language of the user interface in the local settings of the UI.
/// </summary>
public sealed class LanguageService : ILanguageService
{
    /// <summary>
    /// The setting with the language code.
    /// </summary>
    public const string SETTING = "language";

    /// <summary>
    /// The localizer that switches the texts.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// The local settings.
    /// </summary>
    private readonly IUiSettingsStore _settings;

    /// <summary>
    /// Returns the culture of the operating system.
    /// </summary>
    private readonly Func<CultureInfo> _systemCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    /// <param name="settings">The local settings.</param>
    /// <param name="systemCulture">Returns the culture of the operating system; the UI culture of the user by default.</param>
    public LanguageService(ILocalizer localizer, IUiSettingsStore settings, Func<CultureInfo>? systemCulture = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _systemCulture = systemCulture ?? (() => CultureInfo.CurrentUICulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages => _localizer.Languages;

    /// <inheritdoc />
    public LanguageInfo Current => _localizer.Current;

    /// <inheritdoc />
    public void Initialize()
    {
        var stored = _settings.Get(SETTING)?.Trim();
        var known = _localizer.Languages.FirstOrDefault(language => string.Equals(language.Code, stored, StringComparison.OrdinalIgnoreCase));
        if (known is not null)
        {
            _localizer.SetLanguage(known.Code);
            return;
        }

        // The first start (or a stored language that is gone): the operating system decides, once, and the result is
        // stored like any choice of the user. A store that cannot be written must not stop the application: the
        // language is applied and detected again on the next start.
        _localizer.SetLanguage(_localizer.Detect(_systemCulture()).Code);
        try
        {
            _settings.Set(SETTING, _localizer.Current.Code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: see above.
        }
    }

    /// <inheritdoc />
    public void Select(string code)
    {
        _localizer.SetLanguage(code);
        _settings.Set(SETTING, _localizer.Current.Code);
    }
}
