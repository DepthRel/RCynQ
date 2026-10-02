using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One choice of <see cref="TransportKind"/> shown when a node is added.
/// </summary>
/// <remarks>
/// <para>
/// A transport says how the neighbour becomes reachable over the network. It is a different question from
/// <see cref="EndpointMode"/>, which says how this node accepts connections; see <see cref="EndpointModeOption"/>.
/// </para>
/// <para>
/// The choices are created for the dialog that offers them and go away with it. The dialog is modal, so the language
/// cannot change while they are on the screen, and they do not listen for it.
/// </para>
/// </remarks>
public sealed class TransportOption
{
    /// <summary>
    /// The localizer of the texts.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportOption"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="value">The transport.</param>
    public TransportOption(ILocalizer localizer, TransportKind value)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Value = value;
    }

    /// <summary>
    /// Gets the transport.
    /// </summary>
    public TransportKind Value { get; }

    /// <summary>
    /// Gets the title shown in the list.
    /// </summary>
    public string Title => DisplayText.Transport(_localizer, Value);

    /// <summary>
    /// Creates the transports that can be chosen.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <returns>The choices.</returns>
    public static IReadOnlyList<TransportOption> CreateAll(ILocalizer localizer)
        => [new TransportOption(localizer, TransportKind.Direct), new TransportOption(localizer, TransportKind.AmneziaWG)];

    /// <inheritdoc />
    public override string ToString() => Title;
}
