using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch apply &lt;file&gt; [--path &lt;file&gt;]...</c>: brings a patch file into the working tree without committing it.
/// </summary>
[Command("patch apply", Description = "Applies a patch file to the working tree without committing it; a patch that does not fit changes nothing.")]
public sealed class PatchApplyCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchApplyCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchApplyCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the path of the patch file.</summary>
    [CommandParameter(0, Name = "file", Description = "Path of the patch file.")]
    public required string File { get; set; }

    /// <summary>Gets or sets the files of the patch to use.</summary>
    [CommandOption("path", 'p', Description = "A file of the patch to apply (see 'patch files'); may be repeated. Default: the whole patch.")]
    public IReadOnlyList<string>? Paths { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var result = await context.Get<PatchCommandService>().ApplyFileAsync(File, Paths is { Count: > 0 } ? Paths : null, cancellationToken);
        if (result.Output.Length > 0)
        {
            await console.Output.WriteLineAsync(result.Output);
        }

        if (!result.Succeeded)
        {
            throw new CommandException(result.Message, 1);
        }

        await console.Output.WriteLineAsync(result.Message);
    }
}
