using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Transports.Abstractions;
using RCynQ.Transports.Abstractions.Management;

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
            throw new ArgumentException("The fix has no valid 'linkId' parameter.");
        }

        var state = await _links.StartAsync(linkId, cancellationToken);
        return new AutoFixResult(state.IsUsable, $"Link '{state.LinkName}' is {state.Status}: {state.Message}");
    }
}
