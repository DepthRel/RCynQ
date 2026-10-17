using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Cli.Commands.Enrollment;

/// <summary>
/// <c>invite</c>: creates an invitation for a neighbour and prints it, or writes it to a file.
/// </summary>
[Command("invite", Description = "Creates an invitation for a neighbour: a single-use line of text to pass to it privately.")]
public sealed class InviteCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InviteCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public InviteCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the role of the invited node.</summary>
    [CommandOption("role", IsRequired = true, Description = "Role of the invited node: Workstation or Origin.")]
    public NodeRole Role { get; set; }

    /// <summary>Gets or sets how long the invitation stays valid.</summary>
    [CommandOption("hours", Description = "Hours the invitation stays valid (1 to 168, default 24).")]
    public int Hours { get; set; } = 24;

    /// <summary>Gets or sets the file the invitation is written to.</summary>
    [CommandOption("output", 'o', Description = "File for the invitation; printed to stdout when omitted.")]
    public string? Output { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var localizer = context.Get<ILocalizer>();
        var created = await context.Get<InvitationService>().CreateAsync(new CreateInvitationRequest(Role, Hours), context.ConfigPath, cancellationToken);

        await console.Error.WriteLineAsync(localizer.Format("Invite.Created", created.Summary.Role, created.Summary.ExpiresAtUtc.ToLocalTime().ToString("g", localizer.Culture)));
        foreach (var note in created.Notes)
        {
            await console.Error.WriteLineAsync(note);
        }

        if (created.AccessPending)
        {
            await console.Error.WriteLineAsync(localizer.Get("Invite.AccessPending"));
        }

        if (Output is null)
        {
            await console.Output.WriteLineAsync(created.Text);
            return;
        }

        await File.WriteAllTextAsync(Output, created.Text + Environment.NewLine, cancellationToken);
        await console.Error.WriteLineAsync(localizer.Format("Invite.Saved", Path.GetFullPath(Output)));
    }
}
