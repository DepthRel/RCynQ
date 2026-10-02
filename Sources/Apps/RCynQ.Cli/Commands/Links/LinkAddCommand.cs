using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Cli.Commands.Links;

/// <summary>
/// <c>rcynq link add</c>: adds or updates a chain link.
/// </summary>
[Command("link add", Description = "Adds or updates a chain link (relays first, the Workstation/Origin endpoint last).")]
public sealed class LinkAddCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkAddCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public LinkAddCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the link name.</summary>
    [CommandOption("name", IsRequired = true, Description = "Unique link name.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer node identifier.</summary>
    [CommandOption("peer-id", IsRequired = true, Description = "Node identifier of the peer.")]
    public string PeerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer role.</summary>
    [CommandOption("peer-role", IsRequired = true, Description = "workstation, relay or origin.")]
    public NodeRole PeerRole { get; set; }

    /// <summary>Gets or sets the peer host.</summary>
    [CommandOption("host", IsRequired = true, Description = "Peer host or IP address.")]
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the SSH user.</summary>
    [CommandOption("user", IsRequired = true, Description = "SSH user on the peer.")]
    public string User { get; set; } = string.Empty;

    /// <summary>Gets or sets the SSH port.</summary>
    [CommandOption("ssh-port", Description = "Peer SSH port.")]
    public int SshPort { get; set; } = 22;

    /// <summary>Gets or sets the remote port.</summary>
    [CommandOption("remote-port", Description = "Peer patch endpoint port for the endpoint link, next-hop SSH port for relays.")]
    public int RemotePort { get; set; } = 18443;

    /// <summary>Gets or sets the local forward port.</summary>
    [CommandOption("local-port", Description = "Local forward port (0 = ephemeral).")]
    public int LocalPort { get; set; }

    /// <summary>Gets or sets the chain position.</summary>
    [CommandOption("priority", Description = "Chain position; lower comes first.")]
    public int Priority { get; set; }

    /// <summary>Gets or sets the transport.</summary>
    [CommandOption("transport", Description = "Direct or AmneziaWG.")]
    public TransportKind Transport { get; set; } = TransportKind.Direct;

    /// <summary>Gets or sets the fallback transports.</summary>
    [CommandOption("fallback", Description = "Fallback transports, tried in order.")]
    public IReadOnlyList<TransportKind> Fallback { get; set; } = Array.Empty<TransportKind>();

    /// <summary>Gets or sets transport settings.</summary>
    [CommandOption("setting", Description = "Transport setting key=value, e.g. configPath=/etc/amnezia/awg0.conf.")]
    public IReadOnlyList<string> Settings { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the host key fingerprint.</summary>
    [CommandOption("fingerprint", Description = "Expected SSH host key fingerprint (SHA256:...).")]
    public string? Fingerprint { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var options = new ChainLinkOptions
        {
            Name = Name,
            PeerNodeId = PeerId,
            PeerRole = PeerRole,
            Host = Host,
            SshUser = User,
            SshPort = SshPort,
            RemotePort = RemotePort,
            LocalPort = LocalPort,
            Priority = Priority,
            Transport = Transport,
            FallbackTransports = Fallback.ToList(),
            HostKeyFingerprint = Fingerprint
        };

        foreach (var setting in Settings)
        {
            var separator = setting.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new ArgumentException($"Transport setting '{setting}' must have the form key=value.");
            }

            options.TransportSettings[setting[..separator]] = setting[(separator + 1)..];
        }

        await console.Output.WriteAsync(await context.Get<LinkCommandService>().AddAsync(options, cancellationToken));
    }
}
