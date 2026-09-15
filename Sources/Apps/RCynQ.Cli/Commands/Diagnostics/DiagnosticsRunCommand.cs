using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Diagnostics;

/// <summary>
/// <c>rcynq diagnostics run [--fix --yes]</c>.
/// </summary>
[Command("diagnostics run", Description = "Runs the environment checks for the node role and prints suggested fixes.")]
public sealed class DiagnosticsRunCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsRunCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public DiagnosticsRunCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets a value indicating whether safe fixes are applied.</summary>
    [CommandOption("fix", Description = "Apply proposed safe fixes (requires --yes).")]
    public bool Fix { get; set; }

    /// <summary>Gets or sets a value indicating whether fixes are confirmed.</summary>
    [CommandOption("yes", 'y', Description = "Confirm the safe fixes.")]
    public bool Yes { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, allPassed) = await context.Get<DiagnosticsCommandService>().RunAsync(Fix, Yes, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!allPassed)
        {
            throw new CommandException("Some checks failed.", 1);
        }
    }
}
