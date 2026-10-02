using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Sync.Diagnostics.Endpoint;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the local patch endpoint answers on loopback.
/// </summary>
public sealed class PatchEndpointLocalCheck : DiagnosticCheckBase
{
    /// <summary>The endpoint probe.</summary>
    private readonly ILocalEndpointProbe _probe;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchEndpointLocalCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="probe">The endpoint probe.</param>
    public PatchEndpointLocalCheck(ILocalizer localizer, ILocalEndpointProbe probe)
        : base("PatchEndpointLocal", _patchRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var port = context.Options.ListenPort;
        return await _probe.IsHealthyAsync(port, cancellationToken)
            ? Pass(_localizer.Format("Diag.Endpoint.Ok", port))
            : Fail(
                _localizer.Format("Diag.Endpoint.Down", port),
                _localizer.Get("Diag.Endpoint.Fix"),
                new DiagnosticFix(DiagnosticFixKind.RestartLocalEndpoint, _localizer.Get("Diag.Endpoint.Restart"), new Dictionary<string, string> { ["port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
    }
}
