using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the local SSH server accepts connections. Mediator and Dest need it for jumps and Git; Source needs it
/// to receive patches from Dest.
/// </summary>
/// <remarks>
/// A node that accepts connections on its own TLS port (<see cref="EndpointMode.Direct"/>) runs no SSH server by design,
/// so the check passes for it instead of reporting a server that is not supposed to be there.
/// </remarks>
public sealed class SshdListeningCheck : DiagnosticCheckBase
{
    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshdListeningCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="probe">The TCP probe.</param>
    public SshdListeningCheck(ILocalizer localizer, ITcpProbe probe)
        : base("SshdListening", AllRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        if (context.Options.Endpoint.Mode == EndpointMode.Direct)
        {
            return Pass(_localizer.Get("Access.NoSshServer"));
        }

        var port = context.Options.Ssh.LocalSshdPort;
        return await _probe.CanConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(3), cancellationToken)
            ? Pass(_localizer.Format("Diag.Sshd.Ok", port))
            : Fail(_localizer.Format("Diag.Sshd.Down", port), _localizer.Get("Diag.Sshd.Fix"));
    }
}
