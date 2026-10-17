using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Ssh.Server;

namespace RCynQ.Cli.Commands.Ssh;

/// <summary>
/// <c>ssh install-server</c>: installs, starts and opens the SSH server of this computer, asking for administrator
/// rights.
/// </summary>
[Command("ssh install-server", Description = "Installs, starts and opens the SSH server of this computer, asking for administrator rights.")]
public sealed class SshInstallServerCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshInstallServerCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public SshInstallServerCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var result = await context.Get<IServerSetup>().InstallSshServerAsync(cancellationToken);

        await ServerSetupOutput.WriteAsync(result, console);
    }
}
