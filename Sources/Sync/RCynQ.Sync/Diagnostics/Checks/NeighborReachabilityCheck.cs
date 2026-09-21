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

    /// <summary>
    /// Initializes a new instance of the <see cref="NeighborReachabilityCheck"/> class.
    /// </summary>
    /// <param name="probe">The TCP probe.</param>
    public NeighborReachabilityCheck(ITcpProbe probe)
        : base("NeighborReachability", AllRoles)
    {
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        if (context.Chain.Count == 0)
        {
            return Fail("No chain links are configured.", Texts.Get("Diag.Neighbours.AddLinks"));
        }

        var neighbours = context.Node.Role == NodeRole.Mediator ? context.Chain : context.Chain.Take(1).ToArray();
        var unreachable = new List<string>();
        foreach (var link in neighbours)
        {
            if (!await _probe.CanConnectAsync(link.Peer.Host, link.Peer.SshPort, TimeSpan.FromSeconds(5), cancellationToken))
            {
                unreachable.Add($"{link.Name} ({link.Peer.Host}:{link.Peer.SshPort})");
            }
        }

        return unreachable.Count == 0
            ? Pass(Texts.Format("Diag.Neighbours.Ok", string.Join(", ", neighbours.Select(link => link.Name))))
            : Fail(Texts.Format("Diag.Neighbours.Unreachable", string.Join(", ", unreachable)), Texts.Get("Diag.Neighbours.Fix"));
    }
}
