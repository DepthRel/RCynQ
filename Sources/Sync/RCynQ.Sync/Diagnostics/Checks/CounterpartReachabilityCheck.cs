using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Sync.Reachability;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the counterpart endpoint answers along the chain and would take a patch from this node.
/// </summary>
/// <remarks>
/// <see cref="NeighborReachabilityCheck"/> asks the first neighbour directly. This check goes the whole way: through
/// the mediators to the other endpoint, with the link token, exactly as a patch does. It waits for the answer.
/// </remarks>
public sealed class CounterpartReachabilityCheck : DiagnosticCheckBase
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Pings the counterpart endpoint.</summary>
    private readonly EndpointPinger _pinger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterpartReachabilityCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="pinger">Pings the counterpart endpoint.</param>
    public CounterpartReachabilityCheck(ILocalizer localizer, EndpointPinger pinger)
        : base("CounterpartReachability", PatchRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = await _pinger.PingAsync(context.Chain, cancellationToken);
        if (state is null)
        {
            return Fail(_localizer.Get("Diag.Counterpart.None"), _localizer.Get("Diag.Neighbours.AddLinks"));
        }

        // A peer of an earlier version is reachable; that it cannot confirm more is said in the message.
        return state.Status is LinkStatus.Ready or LinkStatus.Degraded
            ? Pass(state.Message)
            : Fail(state.Message ?? string.Empty, _localizer.Get("Diag.Counterpart.Fix"));
    }
}
