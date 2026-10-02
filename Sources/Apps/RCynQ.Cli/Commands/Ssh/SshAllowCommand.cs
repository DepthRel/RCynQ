using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Cli.Commands.Ssh;

/// <summary>
/// <c>rcynq ssh allow</c>: allows the imported neighbours in the <c>authorized_keys</c> file of the local SSH server.
/// </summary>
[Command("ssh allow", Description = "Adds restricted authorized_keys lines for the imported neighbours that connect to this node.")]
public sealed class SshAllowCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshAllowCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public SshAllowCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var result = await context.Get<PeerAccessService>().ApplyAsync(cancellationToken);
        if (result.RequiresElevation)
        {
            throw new CommandException(result.Message, 1);
        }

        await console.Output.WriteLineAsync(result.Message);
        if (result.PeersWithoutKey.Count > 0)
        {
            await console.Output.WriteLineAsync($"No SSH public key in the export of: {string.Join(", ", result.PeersWithoutKey)}. Import a new export that includes the key.");
        }

        if (result.PeersWaitingForCounterpart.Count > 0)
        {
            await console.Output.WriteLineAsync($"Waiting for the opposite endpoint of: {string.Join(", ", result.PeersWaitingForCounterpart)}. Import both the Workstation and the Origin export.");
        }
    }
}
