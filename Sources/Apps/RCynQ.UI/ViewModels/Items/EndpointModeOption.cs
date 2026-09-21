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
    /// Initializes a new instance of the <see cref="EndpointModeOption"/> class.
    /// </summary>
    /// <param name="value">The mode.</param>
    private EndpointModeOption(EndpointMode value)
    {
        Value = value;

        // The choices are shared by the whole application and live as long as it does.
        Texts.Localizer.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Gets the choices in the order they are offered.
    /// </summary>
    public static IReadOnlyList<EndpointModeOption> All { get; } =
    [
        new EndpointModeOption(EndpointMode.SshTunnel),
        new EndpointModeOption(EndpointMode.Direct)
    ];

    /// <summary>
    /// Gets the mode.
    /// </summary>
    public EndpointMode Value { get; }

    /// <summary>
    /// Gets the title shown in the list.
    /// </summary>
    public string Title => Texts.Get($"Endpoint.{Value}.Title");

    /// <summary>
    /// Gets the explanation shown under the list.
    /// </summary>
    public string Hint => Texts.Get($"Endpoint.{Value}.Hint");

    /// <summary>
    /// Gets the choice of a mode.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The choice.</returns>
    public static EndpointModeOption For(EndpointMode mode) => All.First(option => option.Value == mode);

    /// <inheritdoc />
    public override string ToString() => Title;
}
