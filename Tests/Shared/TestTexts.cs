using RCynQ.Localization;

namespace RCynQ.Tests;

/// <summary>
/// The localizers the tests hand to the code under test. A test names the language its expectations are written in;
/// nothing in the process chooses one behind its back.
/// </summary>
/// <remarks>
/// Both localizers are shared by every test of an assembly, so a test that switches the language builds its own
/// <see cref="Localizer"/> instead of switching one of these.
/// </remarks>
internal static class TestTexts
{
    /// <summary>
    /// Gets the localizer of the English texts, the default language of the application.
    /// </summary>
    internal static ILocalizer English { get; } = new Localizer(LanguageCatalog.LoadEmbedded(), "en");

    /// <summary>
    /// Gets the localizer of the Russian texts, which the expectations of the user interface tests are written against.
    /// </summary>
    internal static ILocalizer Russian { get; } = new Localizer(LanguageCatalog.LoadEmbedded(), "ru");
}
