using RCynQ.Core.Diagnostics;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the local patch endpoint answers on loopback.
/// </summary>
public sealed class PatchEndpointLocalCheck : DiagnosticCheckBase
{
    /// <summary>The endpoint probe.</summary>
    private readonly ILocalEndpointProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchEndpointLocalCheck"/> class.
    /// </summary>
    /// <param name="probe">The endpoint probe.</param>
    public PatchEndpointLocalCheck(ILocalEndpointProbe probe)
        : base("PatchEndpointLocal", PatchRoles)
    {
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var port = context.Options.ListenPort;
        return await _probe.IsHealthyAsync(port, cancellationToken)
            ? Pass(Texts.Format("Diag.Endpoint.Ok", port))
            : Fail(
                Texts.Format("Diag.Endpoint.Down", port),
                Texts.Get("Diag.Endpoint.Fix"),
                new DiagnosticFix(DiagnosticFixKind.RestartLocalEndpoint, Texts.Get("Diag.Endpoint.Restart"), new Dictionary<string, string> { ["port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
    }
}
