using RCynQ.Localization;

namespace RCynQ.UI.Services.Localization;

/// <summary>
/// Chooses, applies and remembers the language of the user interface.
/// </summary>
public interface ILanguageService
{
    /// <summary>
    /// Gets the languages that can be chosen.
    /// </summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>
    /// Gets the language in use.
    /// </summary>
    LanguageInfo Current { get; }

    /// <summary>
    /// Applies the language stored locally. On the first start nothing is stored yet: the language of the operating
    /// system is taken when there are texts for it (otherwise the default language) and stored.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Switches the language and stores the choice.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <exception cref="ArgumentException">There are no texts for the language.</exception>
    /// <exception cref="IOException">The choice could not be stored; the language is switched all the same.</exception>
    void Select(string code);
}
