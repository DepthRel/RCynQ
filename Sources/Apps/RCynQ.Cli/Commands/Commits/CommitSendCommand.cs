using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Commits;

/// <summary>
/// <c>rcynq commits send [--up-to &lt;hash&gt;]</c>: sends unsent commits, as they are, to the Origin.
/// </summary>
[Command("commits send", Description = "Sends the unsent commits from the oldest one up to --up-to (all by default) to the Origin, as they are (Workstation).")]
public sealed class CommitSendCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitSendCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public CommitSendCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the last commit to send.</summary>
    [CommandOption("up-to", 'u', Description = "Full name of the last commit to send (see 'commits list'); every unsent commit by default.")]
    public string? UpTo { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<CommitCommandService>().SendAsync(UpTo, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("The commits were not taken by the Origin.", 1);
        }
    }
}
