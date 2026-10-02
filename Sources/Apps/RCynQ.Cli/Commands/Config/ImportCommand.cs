using CliFx.Attributes;
using CliFx.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Localization;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Cli.Commands.Config;

/// <summary>
/// <c>rcynq import &lt;file&gt;</c>: trusts a peer configuration, adds a link to it and allows its SSH key.
/// </summary>
[Command("import", Description = "Imports a peer configuration exported with 'rcynq export', trusts its signing key and allows its SSH key.")]
public sealed class ImportCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public ImportCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the file to import.</summary>
    [CommandParameter(0, Name = "file", Description = "The exported configuration file of the peer.")]
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets what the file is when a node of its role is known already.</summary>
    [CommandOption("as", Description = "What the file is when a node of its role is known already: 'route' (another address of the same node), 'replace' (takes the place of a known node or address) or 'hop' (one more node next to the known ones). Without it such an import changes nothing and lists the choices.")]
    public string? As { get; set; }

    /// <summary>Gets or sets the link whose node is replaced.</summary>
    [CommandOption("link", Description = "With '--as replace': the identifier or name of the link whose node the imported one replaces, when several qualify.")]
    public string? Link { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var decision = ExportImportService.ParseDecision(As);
        Guid? linkId = string.IsNullOrWhiteSpace(Link)
            ? null
            : (await LinkCommandService.FindAsync(context.Get<IChainLinkRepository>(), Link.Trim(), cancellationToken)).Id;
        await console.Output.WriteAsync(await context.Get<ExportImportService>().ImportAsync(File, decision, linkId, cancellationToken));
        if (context.Services.GetService<PeerAccessService>() is not { } access)
        {
            return;
        }

        try
        {
            var result = await access.ApplyAsync(cancellationToken);
            if (result.RequiresElevation)
            {
                await console.Output.WriteLineAsync($"To let the neighbours connect, run 'rcynq ssh allow' as administrator ({result.FilePath}).");
            }
            else if (result.EntryCount > 0)
            {
                await console.Output.WriteLineAsync(result.Message);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            await console.Error.WriteLineAsync($"Neighbour SSH access was not updated: {context.Get<ILocalizer>().Describe(ex)}");
        }
    }
}
