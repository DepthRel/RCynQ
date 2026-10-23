using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Commits;

/// <summary>
/// <c>rcynq commits decline &lt;id&gt;</c>: declines commits from the Workstation that wait on this Origin.
/// </summary>
[Command("commits decline", Description = "Declines commits from the Workstation that wait on this Origin and drops their bundle (Origin).")]
public sealed class CommitDeclineCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitDeclineCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public CommitDeclineCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the delivery.</summary>
    [CommandParameter(0, Name = "patchId", Description = "Identifier of the delivery of commits (see 'patch list').")]
    public Guid PatchId { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<CommitCommandService>().DeclineAsync(PatchId, cancellationToken));
}
