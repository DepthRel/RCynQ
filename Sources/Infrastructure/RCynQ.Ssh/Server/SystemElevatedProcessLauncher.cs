using System.ComponentModel;
using System.Diagnostics;
using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// Runs a command with administrator rights on the running system: through the UAC prompt (<c>runas</c>) on Windows,
/// as an ordinary process elsewhere, where <c>pkexec</c> or <c>osascript</c> asks for the password itself.
/// </summary>
public sealed class SystemElevatedProcessLauncher : IElevatedProcessLauncher
{
    /// <summary>The Windows error of a dismissed UAC prompt (<c>ERROR_CANCELLED</c>).</summary>
    private const int CANCELLED_BY_USER = 1223;

    /// <inheritdoc />
    public async Task<ElevatedProcessExit> RunAsync(ElevatedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Elevation == ElevationKind.WindowsUac
            ? await RunWithUacAsync(command, cancellationToken)
            : await RunCapturedAsync(command, cancellationToken);
    }

    /// <summary>
    /// Runs a command through the UAC prompt; its output cannot be read there.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How the command ended.</returns>
    private static async Task<ElevatedProcessExit> RunWithUacAsync(ElevatedCommand command, CancellationToken cancellationToken)
    {
        // runas needs the shell, and the shell takes one command line; the arguments hold no quotes or spaces.
        var info = new ProcessStartInfo(command.FileName, string.Join(' ', command.Arguments))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"{command.FileName} did not start.");
            await process.WaitForExitAsync(cancellationToken);
            return new ElevatedProcessExit(process.ExitCode, string.Empty);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == CANCELLED_BY_USER)
        {
            return new ElevatedProcessExit(CANCELLED_BY_USER, string.Empty, Declined: true);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Runs a command as an ordinary process and reads its error output.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How the command ended.</returns>
    private static async Task<ElevatedProcessExit> RunCapturedAsync(ElevatedCommand command, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"{command.FileName} did not start.");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await output;
            return new ElevatedProcessExit(process.ExitCode, await error);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
