using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One choice of <see cref="EndpointMode"/> shown in the settings. Its texts are found by the name of the mode
/// (<c>Endpoint.Direct.Title</c>) and follow the language the user chose.
/// </summary>
public sealed class EndpointModeOption : ObservableObject
{
    /// <summary>
    /// The localizer of the texts.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointModeOption"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="value">The mode.</param>
    /// <remarks>
    /// The choice listens to the localizer for as long as the localizer lives, so it belongs to a view model that lives
    /// as long as the application: the settings page and the first-start page.
    /// </remarks>
    public EndpointModeOption(ILocalizer localizer, EndpointMode value)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Value = value;
        _localizer.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Gets the mode.
    /// </summary>
    public EndpointMode Value { get; }

    /// <summary>
    /// Gets the title shown in the list.
    /// </summary>
    public string Title => _localizer.Get($"Endpoint.{Value}.Title");

    /// <summary>
    /// Gets the explanation shown under the list.
    /// </summary>
    public string Hint => _localizer.Get($"Endpoint.{Value}.Hint");

    /// <summary>
    /// Creates the choices in the order they are offered.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <returns>The choices.</returns>
    public static IReadOnlyList<EndpointModeOption> CreateAll(ILocalizer localizer)
        => [new EndpointModeOption(localizer, EndpointMode.SshTunnel), new EndpointModeOption(localizer, EndpointMode.Direct)];

    /// <inheritdoc />
    public override string ToString() => Title;
}
