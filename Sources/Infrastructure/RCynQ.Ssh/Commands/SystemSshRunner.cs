using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;

namespace RCynQ.Ssh.Commands;

/// <summary>
/// <see cref="ISshExecutor"/> that runs the system <c>ssh</c> client.
/// </summary>
/// <remarks>
/// Exit code 255 is reserved by OpenSSH for its own failures (connection, authentication, host key) and is raised as
/// <see cref="SshExecutionException"/>; any other exit code belongs to the remote command and is returned.
/// </remarks>
public sealed class SystemSshRunner : ISshExecutor
{
    /// <summary>
    /// OpenSSH's own failure exit code.
    /// </summary>
    public const int SshFailureExitCode = 255;

    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The command builder.
    /// </summary>
    private readonly SshCommandBuilder _commands;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemSshRunner"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="commands">The command builder.</param>
    public SystemSshRunner(IProcessRunner processRunner, SshCommandBuilder commands)
    {
        _processRunner = processRunner;
        _commands = commands;
    }

    /// <inheritdoc />
    public async Task<SshResult> ExecuteAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string remoteCommand, CancellationToken cancellationToken = default)
    {
        var args = _commands.BuildExec(target, jumps, remoteCommand);
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(new ProcessSpec(_commands.Executable, args, null, null, TimeSpan.FromMinutes(5)), cancellationToken);
        }
        catch (SyncBridgeException ex) when (ex is not SshExecutionException)
        {
            throw new SshExecutionException($"ssh to {target} could not be executed: {ex.Message}", ex);
        }

        if (result.ExitCode == SshFailureExitCode)
        {
            throw new SshExecutionException($"ssh to {target} failed: {result.StandardError.Trim()}", result.ExitCode);
        }

        return new SshResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
