using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One choice of <see cref="TransportKind"/> shown when a node is added.
/// </summary>
/// <remarks>
/// A transport says how the neighbour becomes reachable over the network. It is a different question from
/// <see cref="EndpointMode"/>, which says how this node accepts connections; see <see cref="EndpointModeOption"/>.
/// </remarks>
public sealed class TransportOption : ObservableObject
{
    /// <summary>
    /// Every transport, so a link configured with any of them can be described.
    /// </summary>
    private static readonly IReadOnlyList<TransportOption> Known = [.. Enum.GetValues<TransportKind>().Select(kind => new TransportOption(kind))];

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportOption"/> class.
    /// </summary>
    /// <param name="value">The transport.</param>
    private TransportOption(TransportKind value)
    {
        Value = value;

        // The choices are shared by the whole application and live as long as it does.
        Texts.Localizer.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Gets the transports that can be chosen.
    /// </summary>
    public static IReadOnlyList<TransportOption> All { get; } = [.. Known.Where(option => option.Value is TransportKind.Direct or TransportKind.AmneziaWG)];

    /// <summary>
    /// Gets the transport.
    /// </summary>
    public TransportKind Value { get; }

    /// <summary>
    /// Gets the title shown in the list.
    /// </summary>
    public string Title => DisplayText.Transport(Value);

    /// <summary>
    /// Gets the choice of a transport.
    /// </summary>
    /// <param name="kind">The transport.</param>
    /// <returns>The choice.</returns>
    public static TransportOption For(TransportKind kind) => Known.First(option => option.Value == kind);

    /// <inheritdoc />
    public override string ToString() => Title;
}
