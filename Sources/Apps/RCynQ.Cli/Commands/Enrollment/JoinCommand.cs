using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Localization;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Cli.Commands.Enrollment;

/// <summary>
/// <c>join</c>: joins the node that wrote an invitation, and prints how every stage ended.
/// </summary>
[Command("join", Description = "Joins the node that wrote an invitation (the text, or a file with it).")]
public sealed class JoinCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JoinCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public JoinCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the invitation, or a file that holds it.</summary>
    [CommandParameter(0, Name = "invitation", Description = "The invitation text, or the path of a file with it.")]
    public string Invitation { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var localizer = context.Get<ILocalizer>();
        var text = File.Exists(Invitation) ? await File.ReadAllTextAsync(Invitation, cancellationToken) : Invitation;

        var result = await context.Get<JoinService>().JoinAsync(text, context.ConfigPath, cancellationToken: cancellationToken);

        foreach (var step in result.Steps)
        {
            var mark = step.Succeeded ? "+" : "!";
            await console.Output.WriteLineAsync($"{mark} {localizer.Get("JoinStep." + step.Step)}: {step.Message}");
        }

        if (!result.Joined)
        {
            throw new CommandException(result.Message, 1);
        }

        await console.Output.WriteLineAsync(result.Message);
    }
}
