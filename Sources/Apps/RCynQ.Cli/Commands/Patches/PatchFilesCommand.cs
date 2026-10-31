using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch files &lt;file&gt;</c>: lists the files a patch file changes.
/// </summary>
[Command("patch files", Description = "Lists the files a patch file changes.")]
public sealed class PatchFilesCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFilesCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchFilesCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the path of the patch file.</summary>
    [CommandParameter(0, Name = "file", Description = "Path of the patch file.")]
    public required string File { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<PatchCommandService>().ListFileAsync(File, cancellationToken));
}
