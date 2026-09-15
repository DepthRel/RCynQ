using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Services;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Commands.Config;

/// <summary>
/// <c>rcynq role set source|mediator|dest</c>: writes the role into the configuration file.
/// </summary>
[Command("role set", Description = "Sets the node role (source, mediator or dest) in syncbridge.json.")]
public sealed class RoleSetCommand : ICommand
{
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    [CommandParameter(0, Name = "role", Description = "source, mediator or dest.")]
    public NodeRole Role { get; set; }

    /// <summary>
    /// Gets or sets the repository path.
    /// </summary>
    [CommandOption("repo", Description = "Repository path (required for dest).")]
    public string? RepoPath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [CommandOption("workdir", Description = "Working directory (required for source).")]
    public string? WorkdirPath { get; set; }

    /// <summary>
    /// Gets or sets the node identifier.
    /// </summary>
    [CommandOption("node-id", Description = "Node identifier; derived from the machine name when the file has none.")]
    public string? NodeId { get; set; }

    /// <summary>
    /// Gets or sets the configuration file path.
    /// </summary>
    [CommandOption("config", 'c', Description = "Path of syncbridge.json (default: RCYNQ_CONFIG or ./syncbridge.json).")]
    public string? ConfigPath { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(IConsole console)
    {
        try
        {
            var path = ConfigurationFileEditor.ResolvePath(ConfigPath);
            await console.Output.WriteAsync(await new ConfigCommandService().SetRoleAsync(path, Role, RepoPath, WorkdirPath, NodeId, console.RegisterCancellationHandler()));
        }
        catch (ArgumentException ex)
        {
            throw new CommandException(ex.Message, 2);
        }
        catch (SyncBridgeException ex)
        {
            throw new CommandException(ex.Message, 1);
        }
    }
}
