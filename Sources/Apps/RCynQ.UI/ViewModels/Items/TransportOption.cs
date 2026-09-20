using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One choice of <see cref="TransportKind"/> shown when a node is added.
/// </summary>
/// <remarks>
/// A transport says how the neighbour becomes reachable over the network. It is a different question from
/// <see cref="EndpointMode"/>, which says how this node accepts connections; see <see cref="EndpointModeOption"/>.
/// </remarks>
/// <param name="Value">The transport.</param>
/// <param name="Title">The title shown in the list.</param>
public sealed record TransportOption(TransportKind Value, string Title)
{
    /// <summary>
    /// Gets the transports that can be chosen.
    /// </summary>
    public static IReadOnlyList<TransportOption> All { get; } = new[]
    {
        new TransportOption(TransportKind.Direct, DisplayText.Transport(TransportKind.Direct)),
        new TransportOption(TransportKind.AmneziaWG, DisplayText.Transport(TransportKind.AmneziaWG))
    };

    /// <summary>
    /// Gets the choice of a transport.
    /// </summary>
    /// <param name="kind">The transport.</param>
    /// <returns>The choice.</returns>
    public static TransportOption For(TransportKind kind)
        => All.FirstOrDefault(option => option.Value == kind) ?? new TransportOption(kind, DisplayText.Transport(kind));

    /// <inheritdoc />
    public override string ToString() => Title;
}
