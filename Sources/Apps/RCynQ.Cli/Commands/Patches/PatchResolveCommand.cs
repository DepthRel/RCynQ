using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Core.Enums;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch resolve &lt;id&gt; --policy KeepLocal|ApplyIncomingOverLocal|UseGit</c>.
/// </summary>
[Command("patch resolve", Description = "Resolves an incoming patch that conflicts with local changes.")]
public sealed class PatchResolveCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchResolveCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchResolveCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the patch identifier.</summary>
    [CommandParameter(0, Name = "patchId", Description = "Identifier of the patch (see 'patch list').")]
    public Guid PatchId { get; set; }

    /// <summary>Gets or sets the policy.</summary>
    [CommandOption("policy", 'p', IsRequired = true, Description = "KeepLocal, ApplyIncomingOverLocal or UseGit.")]
    public ConflictPolicy Policy { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<PatchCommandService>().ResolveAsync(PatchId, Policy, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("Conflicts remain; resolve them and run the command again.", 1);
        }
    }
}
