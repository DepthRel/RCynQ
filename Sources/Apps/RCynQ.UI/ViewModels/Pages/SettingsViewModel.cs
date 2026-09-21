using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The settings tab: the settings of the interface itself, at the moment its language.
/// </summary>
public sealed class SettingsViewModel : PageViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="language">The language selector, shared with the other places that show it.</param>
    /// <param name="settingsPath">The file the settings are stored in, shown to the user, or <see langword="null"/>.</param>
    public SettingsViewModel(LanguageViewModel language, string? settingsPath = null)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        SettingsPath = settingsPath;
    }

    /// <summary>
    /// Gets the language selector.
    /// </summary>
    public LanguageViewModel Language { get; }

    /// <summary>
    /// Gets the file the settings are stored in, or <see langword="null"/> when it is not known.
    /// </summary>
    public string? SettingsPath { get; }

    /// <summary>
    /// Gets a value indicating whether the location of the settings is shown.
    /// </summary>
    public bool HasSettingsPath => !string.IsNullOrEmpty(SettingsPath);

    /// <summary>
    /// Gets the description of where the settings are stored.
    /// </summary>
    public string StoredInText => SettingsPath is null ? string.Empty : Texts.Format("Settings.StoredIn", SettingsPath);

    /// <inheritdoc />
    public override Task RefreshAsync() => Task.CompletedTask;
}
