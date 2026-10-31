using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch save &lt;file&gt; [--path &lt;file&gt;]... [--discard] [--force]</c>: saves the changes to a patch file.
/// </summary>
[Command("patch save", Description = "Saves the changed files to a patch file that git apply understands.")]
public sealed class PatchSaveCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchSaveCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchSaveCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the path of the patch file.</summary>
    [CommandParameter(0, Name = "file", Description = "Path of the patch file to write.")]
    public required string File { get; set; }

    /// <summary>Gets or sets the files to save.</summary>
    [CommandOption("path", 'p', Description = "A file to save, relative to the working tree; may be repeated. Default: every changed file.")]
    public IReadOnlyList<string>? Paths { get; set; }

    /// <summary>Gets or sets a value indicating whether the changes leave the working tree.</summary>
    [CommandOption("discard", Description = "Takes the saved changes out of the working tree after the file is written.")]
    public bool Discard { get; set; }

    /// <summary>Gets or sets a value indicating whether an existing file is replaced.</summary>
    [CommandOption("force", 'f', Description = "Replaces an existing file.")]
    public bool Force { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<PatchCommandService>().SaveFileAsync(File, Paths is { Count: > 0 } ? Paths : null, Discard, Force, cancellationToken);
        if (!succeeded)
        {
            throw new CommandException(text, 1);
        }

        await console.Output.WriteAsync(text);
    }
}
