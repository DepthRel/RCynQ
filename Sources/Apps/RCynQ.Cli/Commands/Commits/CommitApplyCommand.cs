using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Commits;

/// <summary>
/// <c>rcynq commits apply &lt;id&gt;</c>: applies commits from the Workstation that wait on this Origin.
/// </summary>
[Command("commits apply", Description = "Applies commits from the Workstation that wait until the working tree is clean (Origin).")]
public sealed class CommitApplyCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitApplyCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public CommitApplyCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the delivery.</summary>
    [CommandParameter(0, Name = "patchId", Description = "Identifier of the delivery of commits (see 'patch list').")]
    public Guid PatchId { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<CommitCommandService>().ApplyAsync(PatchId, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("The commits still wait; deal with the reason and run the command again.", 1);
        }
    }
}
