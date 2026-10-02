using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks TCP reachability of the direct neighbours: the first chain link on Source and Dest, every link on a Mediator.
/// </summary>
public sealed class NeighborReachabilityCheck : DiagnosticCheckBase
{
    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NeighborReachabilityCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="probe">The TCP probe.</param>
    public NeighborReachabilityCheck(ILocalizer localizer, ITcpProbe probe)
        : base("NeighborReachability", AllRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        if (context.Chain.Count == 0)
        {
            return Fail(_localizer.Get("Diag.Chain.None"), _localizer.Get("Diag.Neighbours.AddLinks"));
        }

        var neighbours = context.Node.Role == NodeRole.Mediator ? context.Chain : context.Chain.Take(1).ToArray();
        var unreachable = new List<string>();
        foreach (var link in neighbours)
        {
            // The port the neighbour really listens on: its own TLS port when it runs no SSH server.
            var port = link.Peer.ReachablePort;
            if (!await _probe.CanConnectAsync(link.Peer.Host, port, TimeSpan.FromSeconds(5), cancellationToken))
            {
                unreachable.Add($"{link.Name} ({link.Peer.Host}:{port})");
            }
        }

        return unreachable.Count == 0
            ? Pass(_localizer.Format("Diag.Neighbours.Ok", string.Join(", ", neighbours.Select(link => link.Name))))
            : Fail(_localizer.Format("Diag.Neighbours.Unreachable", string.Join(", ", unreachable)), _localizer.Get("Diag.Neighbours.Fix"));
    }
}
