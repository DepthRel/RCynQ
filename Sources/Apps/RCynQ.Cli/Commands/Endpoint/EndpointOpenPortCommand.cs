using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Commands.Ssh;
using RCynQ.Cli.Hosting;
using RCynQ.Core.Config;
using RCynQ.Localization;
using RCynQ.Ssh.Server;

namespace RCynQ.Cli.Commands.Endpoint;

/// <summary>
/// <c>endpoint open-port</c>: lets the TLS port of this node through the firewall, asking for administrator rights.
/// </summary>
[Command("endpoint open-port", Description = "Lets the TLS port of this node through the firewall, asking for administrator rights.")]
public sealed class EndpointOpenPortCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointOpenPortCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public EndpointOpenPortCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the port to open; the TLS port of the configuration when omitted.</summary>
    [CommandOption("port", Description = "The port to open (default: the TLS port of the configuration).")]
    public int? Port { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var port = Port ?? context.Get<SyncBridgeOptions>().Endpoint.Port;
        if (port is < 1 or > 65535)
        {
            throw new ArgumentException(context.Get<ILocalizer>().Get("Export.Error.Port"), nameof(Port));
        }

        var result = await context.Get<IServerSetup>().OpenEndpointPortAsync(port, cancellationToken);

        await ServerSetupOutput.WriteAsync(result, console);
    }
}
