using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Git;

/// <summary>
/// <c>rcynq git switch &lt;branch&gt; [--carry | --save-patch &lt;file&gt; [--force]]</c>: switches the working tree to another branch.
/// </summary>
[Command("git switch", Description = "Switches the working tree to a local branch or to a branch fetched from Origin (Workstation only; Origin is not touched).")]
public sealed class GitSwitchCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitSwitchCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public GitSwitchCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the branch to switch to.</summary>
    [CommandParameter(0, Name = "branch", Description = "A local branch, or a branch of Origin that becomes local.")]
    public required string Branch { get; set; }

    /// <summary>Gets or sets a value indicating whether uncommitted changes are carried over.</summary>
    [CommandOption("carry", Description = "Carries uncommitted changes over to the new branch; Git refuses when they collide with it.")]
    public bool Carry { get; set; }

    /// <summary>Gets or sets the file uncommitted changes are saved to before they are taken out.</summary>
    [CommandOption("save-patch", Description = "Saves uncommitted changes to this patch file and takes them out before switching.")]
    public string? SavePatch { get; set; }

    /// <summary>Gets or sets a value indicating whether an existing patch file is replaced.</summary>
    [CommandOption("force", 'f', Description = "Replaces an existing patch file given with --save-patch.")]
    public bool Force { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<GitCommandService>().SwitchAsync(Branch, Carry, SavePatch, Force, cancellationToken);
        if (!succeeded)
        {
            throw new CommandException(text, 1);
        }

        await console.Output.WriteAsync(text);
    }
}
