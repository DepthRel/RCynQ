using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Services;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Commands.Config;

/// <summary>
/// <c>rcynq config validate</c>: reports every problem of the configuration file.
/// </summary>
[Command("config validate", Description = "Validates syncbridge.json and lists every problem.")]
public sealed class ConfigValidateCommand : ICommand
{
    /// <summary>
    /// Gets or sets the configuration file path.
    /// </summary>
    [CommandOption("config", 'c', Description = "Path of syncbridge.json (default: RCYNQ_CONFIG or ./syncbridge.json).")]
    public string? ConfigPath { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var path = ConfigurationFileEditor.ResolvePath(ConfigPath);
        var errors = await new ConfigCommandService().ValidateAsync(path, console.RegisterCancellationHandler());
        if (errors.Count > 0)
        {
            throw new CommandException($"{path} is invalid:{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}", 2);
        }

        await console.Output.WriteLineAsync($"{path} is valid.");
    }
}
