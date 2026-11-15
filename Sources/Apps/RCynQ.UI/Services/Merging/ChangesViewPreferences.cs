using RCynQ.UI.Enums;
using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// <see cref="IChangesViewPreferences"/> kept in the settings of the interface.
/// </summary>
public sealed class ChangesViewPreferences : IChangesViewPreferences
{
    /// <summary>The name of the setting.</summary>
    public const string SETTING = "changesView";

    /// <summary>The settings of the interface.</summary>
    private readonly IUiSettingsStore _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangesViewPreferences"/> class.
    /// </summary>
    /// <param name="settings">The settings of the interface.</param>
    public ChangesViewPreferences(IUiSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public ChangesViewMode Mode => Enum.TryParse<ChangesViewMode>(_settings.Get(SETTING), out var mode) && Enum.IsDefined(mode) ? mode : ChangesViewMode.BuiltIn;

    /// <inheritdoc />
    public void SetMode(ChangesViewMode mode) => _settings.Set(SETTING, mode.ToString());
}
