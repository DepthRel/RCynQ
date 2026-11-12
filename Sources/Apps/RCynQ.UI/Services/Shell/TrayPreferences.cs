using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Shell;

/// <summary>
/// <see cref="ITrayPreferences"/> kept in the settings of the user interface.
/// </summary>
/// <remarks>
/// Until the user chooses, closing to the tray is on where every desktop shows a tray icon (Windows, macOS) and off on
/// Linux, where a desktop without a tray (GNOME without the AppIndicator extension) would leave a hidden window with no
/// way back to it.
/// </remarks>
public sealed class TrayPreferences : ITrayPreferences
{
    /// <summary>
    /// The name of the setting in the settings of the user interface.
    /// </summary>
    public const string SETTING = "CloseToTray";

    /// <summary>
    /// The settings of the user interface.
    /// </summary>
    private readonly IUiSettingsStore _settings;

    /// <summary>
    /// The value used until the user chooses.
    /// </summary>
    private readonly bool _default;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayPreferences"/> class.
    /// </summary>
    /// <param name="settings">The settings of the user interface.</param>
    /// <param name="isLinux">Whether the application runs on Linux; <see langword="null"/> asks the operating system.</param>
    public TrayPreferences(IUiSettingsStore settings, bool? isLinux = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _default = !(isLinux ?? OperatingSystem.IsLinux());
    }

    /// <inheritdoc />
    public bool CloseToTray => bool.TryParse(_settings.Get(SETTING), out var stored) ? stored : _default;

    /// <inheritdoc />
    public void SetCloseToTray(bool enabled) => _settings.Set(SETTING, enabled ? bool.TrueString : bool.FalseString);
}
