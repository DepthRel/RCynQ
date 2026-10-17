using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Localization;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Cli.Commands.Enrollment;

/// <summary>
/// <c>invitation list</c>: lists the invitations of this node and what became of them.
/// </summary>
[Command("invitation list", Description = "Lists the invitations of this node: open, used, revoked or expired.")]
public sealed class InvitationListCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationListCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public InvitationListCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var localizer = context.Get<ILocalizer>();
        var now = context.Get<TimeProvider>().GetUtcNow();

        foreach (var invitation in await context.Get<InvitationService>().ListAsync(cancellationToken))
        {
            var state = invitation.UsedBy is { } node ? localizer.Format("Invitation.State.Used", node)
                : invitation.IsRevoked ? localizer.Get("Invitation.State.Revoked")
                : invitation.ExpiresAtUtc <= now ? localizer.Get("Invitation.State.Expired")
                : localizer.Get("Invitation.State.Open");
            var until = invitation.ExpiresAtUtc.ToLocalTime().ToString("g", localizer.Culture);
            await console.Output.WriteLineAsync($"{invitation.Id:N}  {invitation.Role,-11}  {until}  {state}");
        }
    }
}
