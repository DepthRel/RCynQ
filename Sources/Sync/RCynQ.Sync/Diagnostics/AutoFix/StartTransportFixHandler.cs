using RCynQ.Core.Diagnostics;
using RCynQ.Transports.Abstractions.Management;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Starts a transport link.
/// </summary>
public sealed class StartTransportFixHandler : IAutoFixHandler
{
    /// <summary>The link manager.</summary>
    private readonly ILinkManager _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartTransportFixHandler"/> class.
    /// </summary>
    /// <param name="links">The link manager.</param>
    public StartTransportFixHandler(ILinkManager links)
    {
        _links = links;
    }

    /// <inheritdoc />
    public DiagnosticFixKind Kind => DiagnosticFixKind.StartTransport;

    /// <inheritdoc />
    public async Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken)
    {
        if (!fix.Parameters.TryGetValue("linkId", out var text) || !Guid.TryParse(text, out var linkId))
        {
            throw new ArgumentException(Texts.Get("Fix.NoLinkId"));
        }

        var state = await _links.StartAsync(linkId, cancellationToken);
        return new AutoFixResult(state.IsUsable, Texts.Format("Fix.LinkState", state.LinkName, state.Status, state.Message));
    }
}
