using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

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
            ? Pass($"Patch endpoint answers on 127.0.0.1:{port}.")
            : Fail(
                $"No RCynQ service answers on 127.0.0.1:{port}.",
                "Start RCynQ.Service with the same configuration.",
                new DiagnosticFix(DiagnosticFixKind.RestartLocalEndpoint, "Restart the local RCynQ endpoint.", new Dictionary<string, string> { ["port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
    }
}
