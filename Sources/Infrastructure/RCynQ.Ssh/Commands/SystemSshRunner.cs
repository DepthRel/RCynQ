using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

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
    public const int SSH_FAILURE_EXIT_CODE = 255;

    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The command builder.
    /// </summary>
    private readonly SshCommandBuilder _commands;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemSshRunner"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="commands">The command builder.</param>
    public SystemSshRunner(ILocalizer localizer, IProcessRunner processRunner, SshCommandBuilder commands)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _processRunner = processRunner;
        _commands = commands;
    }

    /// <inheritdoc />
    public Task<SshResult> ExecuteAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string remoteCommand, CancellationToken cancellationToken = default)
        => RunAsync(target, _commands.BuildExec(target, jumps, remoteCommand), cancellationToken);

    /// <inheritdoc />
    public Task<SshResult> ExecuteWithKeyAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string identityFile, string remoteCommand, CancellationToken cancellationToken = default)
        => RunAsync(target, _commands.BuildExec(target, jumps, identityFile, remoteCommand), cancellationToken);

    /// <summary>
    /// Runs <c>ssh</c> with the arguments and turns its own failures into <see cref="SshExecutionException"/>.
    /// </summary>
    /// <param name="target">The SSH endpoint, named in the messages.</param>
    /// <param name="args">The arguments.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <returns>The captured result.</returns>
    private async Task<SshResult> RunAsync(SshEndpoint target, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(new ProcessSpec(_commands.Executable, args, _commands.WorkingDirectory, null, TimeSpan.FromMinutes(5)), cancellationToken);
        }
        catch (SyncBridgeException ex) when (ex is not SshExecutionException)
        {
            throw new SshExecutionException(_localizer.Format("Ssh.NotExecuted", target, _localizer.Describe(ex)), ex);
        }

        if (result.ExitCode == SSH_FAILURE_EXIT_CODE)
        {
            throw new SshExecutionException(_localizer.Format("Ssh.Failed", target, result.StandardError.Trim()), result.ExitCode);
        }

        return new SshResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
