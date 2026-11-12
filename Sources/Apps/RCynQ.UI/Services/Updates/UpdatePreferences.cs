using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Updates;

/// <summary>
/// <see cref="IUpdatePreferences"/> kept in the settings of the user interface. The check is on until the user turns
/// it off.
/// </summary>
public sealed class UpdatePreferences : IUpdatePreferences
{
    /// <summary>
    /// The name of the setting that turns the check on or off.
    /// </summary>
    public const string CHECK_SETTING = "CheckForUpdates";

    /// <summary>
    /// The name of the setting with the version the user asked not to be reminded of.
    /// </summary>
    public const string SKIPPED_SETTING = "SkippedVersion";

    /// <summary>
    /// The settings of the user interface.
    /// </summary>
    private readonly IUiSettingsStore _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePreferences"/> class.
    /// </summary>
    /// <param name="settings">The settings of the user interface.</param>
    public UpdatePreferences(IUiSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public bool CheckEnabled => !bool.TryParse(_settings.Get(CHECK_SETTING), out var stored) || stored;

    /// <inheritdoc />
    public Version? SkippedVersion => UpdateManifest.TryParse(_settings.Get(SKIPPED_SETTING));

    /// <inheritdoc />
    public void SetCheckEnabled(bool enabled) => _settings.Set(CHECK_SETTING, enabled ? bool.TrueString : bool.FalseString);

    /// <inheritdoc />
    public void Skip(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _settings.Set(SKIPPED_SETTING, version.ToString(3));
    }
}
