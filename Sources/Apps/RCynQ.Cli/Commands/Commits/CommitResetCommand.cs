using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Core.Enums;

namespace RCynQ.Cli.Commands.Commits;

/// <summary>
/// <c>rcynq commits reset --to &lt;hash&gt; --mode Soft|Mixed|Hard</c>: rolls back unsent commits.
/// </summary>
[Command("commits reset", Description = "Rolls back unsent commits down to --to, as git reset does; refused while the working tree has changes (Workstation).")]
public sealed class CommitResetCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitResetCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public CommitResetCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the commit that becomes HEAD.</summary>
    [CommandOption("to", 't', IsRequired = true, Description = "The commit that becomes HEAD: an unsent commit, or the commit of the Origin to roll back all of them.")]
    public string To { get; set; } = string.Empty;

    /// <summary>Gets or sets what happens to the changes of the dropped commits.</summary>
    [CommandOption("mode", 'm', IsRequired = true, Description = "Soft (changes stay staged), Mixed (stay in the working tree) or Hard (discarded).")]
    public CommitResetMode Mode { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<CommitCommandService>().ResetAsync(To, Mode, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("The commits were not rolled back.", 1);
        }
    }
}
