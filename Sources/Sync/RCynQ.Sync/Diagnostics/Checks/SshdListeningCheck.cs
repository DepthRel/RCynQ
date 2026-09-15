using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the local SSH server accepts connections. Mediator and Dest need it for jumps and Git; Source needs it
/// to receive patches from Dest.
/// </summary>
public sealed class SshdListeningCheck : DiagnosticCheckBase
{
    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshdListeningCheck"/> class.
    /// </summary>
    /// <param name="probe">The TCP probe.</param>
    public SshdListeningCheck(ITcpProbe probe)
        : base("SshdListening", AllRoles)
    {
        _probe = probe;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var port = context.Options.Ssh.LocalSshdPort;
        return await _probe.CanConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(3), cancellationToken)
            ? Pass($"sshd accepts connections on port {port}.")
            : Fail($"Nothing listens on 127.0.0.1:{port}.", "Install and start the OpenSSH server (sshd). RCynQ does not change system services automatically.");
    }
}
