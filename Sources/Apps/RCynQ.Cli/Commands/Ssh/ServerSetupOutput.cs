using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Ssh.Server;

namespace RCynQ.Cli.Commands.Ssh;

/// <summary>
/// Prints how installing the SSH server or opening a port ended, and fails the command when the computer is not ready.
/// </summary>
internal static class ServerSetupOutput
{
    /// <summary>
    /// Prints the result; the commands by hand follow the message on their own lines.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="console">The console.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="CommandException">The computer is not ready (exit code 1).</exception>
    public static async ValueTask WriteAsync(ServerSetupResult result, IConsole console)
    {
        var text = string.IsNullOrEmpty(result.ManualCommands) ? result.Message : $"{result.Message}{Environment.NewLine}{result.ManualCommands}";

        if (!result.IsReady)
        {
            throw new CommandException(text, 1);
        }

        await console.Output.WriteLineAsync(text);
    }
}
