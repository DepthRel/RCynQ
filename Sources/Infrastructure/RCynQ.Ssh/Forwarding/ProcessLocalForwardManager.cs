using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Network;
using RCynQ.Localization;

namespace RCynQ.Ssh.Forwarding;

/// <summary>
/// <see cref="ILocalForwardManager"/> that runs one <c>ssh -N -L</c> process per local port.
/// </summary>
/// <remarks>
/// A forward counts as started once its loopback port accepts connections; OpenSSH binds local forwards only after
/// authentication succeeded. If the process exits first, its standard error is reported.
/// </remarks>
public sealed class ProcessLocalForwardManager : ILocalForwardManager, IDisposable
{
    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The TCP probe.
    /// </summary>
    private readonly ITcpProbe _probe;

    /// <summary>
    /// The port allocator.
    /// </summary>
    private readonly IPortAllocator _ports;

    /// <summary>
    /// The command builder.
    /// </summary>
    private readonly SshCommandBuilder _commands;

    /// <summary>
    /// The maximum time to wait for a forward to become ready.
    /// </summary>
    private readonly TimeSpan _startupTimeout;

    /// <summary>
    /// Running forwards by local port.
    /// </summary>
    private readonly ConcurrentDictionary<int, IRunningProcess> _forwards = new();

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessLocalForwardManager"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="ports">The port allocator.</param>
    /// <param name="commands">The command builder.</param>
    /// <param name="options">The SSH options; the startup timeout is the connect timeout plus 20 seconds for jump hosts.</param>
    public ProcessLocalForwardManager(ILocalizer localizer, IProcessRunner processRunner, ITcpProbe probe, IPortAllocator ports, SshCommandBuilder commands, SshClientOptions options)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _processRunner = processRunner;
        _probe = probe;
        _ports = ports;
        _commands = commands;
        _startupTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds + 20);
    }

    /// <inheritdoc />
    public async Task<int> StartAsync(
        SshEndpoint target,
        IReadOnlyList<SshEndpoint> jumps,
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(jumps);
        var port = localPort == 0 ? _ports.AllocateLoopbackPort() : localPort;
        if (IsRunning(port))
        {
            throw new ArgumentException($"A forward is already running on local port {port}.", nameof(localPort));
        }

        // A forward that ended by itself (the connection dropped) is still on record; release it before the port is reused.
        if (_forwards.TryRemove(port, out var ended))
        {
            ended.Dispose();
        }

        // The forward counts as ready as soon as something answers on the port. If another program already listens there,
        // it would be that program, answering while ssh is still connecting: the requests meant for the peer would go to it.
        if (await _probe.CanConnectAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(500), cancellationToken))
        {
            throw new SshExecutionException(_localizer.Format("Forward.PortInUse", port, target));
        }

        var args = _commands.BuildLocalForward(target, jumps, port, remoteHost, remotePort);
        var process = _processRunner.Start(new Core.Abstractions.Processes.ProcessSpec(_commands.Executable, args, _commands.WorkingDirectory, null, null));
        var deadline = DateTime.UtcNow + _startupTimeout;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    throw new SshExecutionException(
                        _localizer.Format("Forward.Exited", port, target, remoteHost, remotePort, process.StandardError.Trim()),
                        process.ExitCode ?? -1);
                }

                if (await _probe.CanConnectAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(500), cancellationToken))
                {
                    _forwards[port] = process;
                    return port;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new SshExecutionException(_localizer.Format("Forward.NotReady", port, target, _startupTimeout.TotalSeconds));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(int localPort, CancellationToken cancellationToken = default)
    {
        if (!_forwards.TryRemove(localPort, out var process))
        {
            return;
        }

        process.Kill();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The process did not report its exit in time; it was killed already.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <inheritdoc />
    public bool IsRunning(int localPort) => _forwards.TryGetValue(localPort, out var process) && !process.HasExited;

    /// <summary>
    /// Stops all forwards.
    /// </summary>
    public void Dispose()
    {
        foreach (var port in _forwards.Keys.ToArray())
        {
            if (_forwards.TryRemove(port, out var process))
            {
                process.Dispose();
            }
        }
    }
}
