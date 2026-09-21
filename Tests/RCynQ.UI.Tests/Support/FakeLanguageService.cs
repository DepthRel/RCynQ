using RCynQ.Localization;
using RCynQ.UI.Services.Localization;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// An <see cref="ILanguageService"/> that offers English and Russian and remembers what was selected.
/// </summary>
internal sealed class FakeLanguageService : ILanguageService
{
    /// <summary>
    /// The English language.
    /// </summary>
    private static readonly LanguageInfo English = new("en", "English");

    /// <summary>
    /// The Russian language.
    /// </summary>
    private static readonly LanguageInfo Russian = new("ru", "Русский");

    /// <summary>
    /// Gets or sets an exception thrown by <see cref="Select"/>, or <see langword="null"/>.
    /// </summary>
    public Exception? SelectFailure { get; set; }

    /// <summary>
    /// Gets the codes passed to <see cref="Select"/>.
    /// </summary>
    public List<string> Selected { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages { get; } = [English, Russian];

    /// <inheritdoc />
    public LanguageInfo Current { get; private set; } = English;

    /// <inheritdoc />
    public void Initialize()
    {
    }

    /// <inheritdoc />
    public void Select(string code)
    {
        Selected.Add(code);
        Current = Languages.First(language => language.Code == code);
        if (SelectFailure is not null)
        {
            throw SelectFailure;
        }
    }
}
