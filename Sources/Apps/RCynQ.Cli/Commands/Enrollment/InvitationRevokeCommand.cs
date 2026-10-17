using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Localization;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Cli.Commands.Enrollment;

/// <summary>
/// <c>invitation revoke</c>: revokes an open invitation and removes its one-time key.
/// </summary>
[Command("invitation revoke", Description = "Revokes an open invitation; its one-time key is removed from authorized_keys.")]
public sealed class InvitationRevokeCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationRevokeCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public InvitationRevokeCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the identifier of the invitation, as <c>invitation list</c> prints it.</summary>
    [CommandParameter(0, Name = "id", Description = "The identifier of the invitation.")]
    public Guid Id { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var localizer = context.Get<ILocalizer>();

        if (!await context.Get<InvitationService>().RevokeAsync(Id, cancellationToken))
        {
            throw new CommandException(localizer.Format("Invitation.NotOpen", Id.ToString("N")), 1);
        }

        await console.Output.WriteLineAsync(localizer.Format("Invitation.Revoked", Id.ToString("N")));
    }
}
