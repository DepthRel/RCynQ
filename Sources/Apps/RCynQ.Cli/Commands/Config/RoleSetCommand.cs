using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Services;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Commands.Config;

/// <summary>
/// <c>rcynq role set workstation|relay|origin</c>: writes the role into the configuration file.
/// </summary>
[Command("role set", Description = "Sets the node role (workstation, relay or origin) in syncbridge.json.")]
public sealed class RoleSetCommand : ICommand
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleSetCommand"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public RoleSetCommand(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    [CommandParameter(0, Name = "role", Description = "workstation, relay or origin.")]
    public NodeRole Role { get; set; }

    /// <summary>
    /// Gets or sets the repository path.
    /// </summary>
    [CommandOption("repo", Description = "Repository path (required for origin).")]
    public string? RepoPath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [CommandOption("workdir", Description = "Working directory (required for workstation).")]
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
            await console.Output.WriteAsync(await new ConfigCommandService(_localizer).SetRoleAsync(path, Role, RepoPath, WorkdirPath, NodeId, console.RegisterCancellationHandler()));
        }
        catch (ArgumentException ex)
        {
            throw new CommandException(_localizer.Describe(ex), 2);
        }
        catch (SyncBridgeException ex)
        {
            throw new CommandException(_localizer.Describe(ex), 1);
        }
    }
}
