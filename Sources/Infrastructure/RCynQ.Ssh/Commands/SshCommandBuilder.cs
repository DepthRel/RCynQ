using RCynQ.Core.Config;
using RCynQ.Core.Validation;
using RCynQ.Ssh.Routing;

namespace RCynQ.Ssh.Commands;

/// <summary>
/// Builds <c>ssh</c> argument lists.
/// </summary>
/// <remarks>
/// All connections use <c>BatchMode=yes</c> (never prompt), <c>StrictHostKeyChecking=accept-new</c> (pin on first use,
/// refuse changed keys) and a connect timeout. The destination is preceded by <c>--</c>, so even a malformed value can
/// never be parsed as an option; endpoints are additionally validated by <see cref="SshEndpoint"/>.
/// </remarks>
public sealed class SshCommandBuilder
{
    /// <summary>
    /// The SSH client options.
    /// </summary>
    private readonly SshClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshCommandBuilder"/> class.
    /// </summary>
    /// <param name="options">The SSH client options.</param>
    public SshCommandBuilder(SshClientOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Gets the <c>ssh</c> executable.
    /// </summary>
    public string Executable => _options.SshExecutable;

    /// <summary>
    /// Builds the options shared by every connection.
    /// </summary>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> BuildCommonOptions()
    {
        var args = new List<string>
        {
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", $"ConnectTimeout={_options.ConnectTimeoutSeconds}"
        };

        if (!string.IsNullOrWhiteSpace(_options.IdentityFile))
        {
            args.AddRange(new[] { "-i", _options.IdentityFile, "-o", "IdentitiesOnly=yes" });
        }

        if (KnownHostsOption(_options.KnownHostsFile) is { } knownHosts)
        {
            args.AddRange(new[] { "-o", knownHosts });
        }

        return args;
    }

    /// <summary>
    /// Builds the <c>UserKnownHostsFile</c> option.
    /// </summary>
    /// <param name="path">The <c>known_hosts</c> path, or <see langword="null"/>.</param>
    /// <returns>
    /// The option, or <see langword="null"/> when no path is set or the path contains whitespace or quotes (OpenSSH splits
    /// the value at whitespace, so such a path falls back to the default <c>known_hosts</c>).
    /// </returns>
    public static string? KnownHostsOption(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' || char.IsControl(c)))
        {
            return null;
        }

        return "UserKnownHostsFile=" + path.Replace('\\', '/');
    }

    /// <summary>
    /// Builds a remote command execution.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="jumps">The jump hosts.</param>
    /// <param name="remoteCommand">The remote command (single argument).</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">The remote command is blank or contains control characters.</exception>
    public IReadOnlyList<string> BuildExec(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string remoteCommand)
    {
        ArgumentNullException.ThrowIfNull(target);
        var command = Guard.NotBlank(remoteCommand, nameof(remoteCommand));
        if (command.Any(char.IsControl))
        {
            throw new ArgumentException("Remote command must not contain control characters.", nameof(remoteCommand));
        }

        return BuildCommonOptions()
            .Concat(new[] { "-T" })
            .Concat(ProxyJumpBuilder.Build(jumps))
            .Concat(new[] { "-p", target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--", target.Destination, command })
            .ToArray();
    }

    /// <summary>
    /// Builds a background local port forward bound to the loopback interface.
    /// </summary>
    /// <param name="target">The SSH endpoint that opens the forwarded connection.</param>
    /// <param name="jumps">The jump hosts.</param>
    /// <param name="localPort">The local loopback port (1..65535).</param>
    /// <param name="remoteHost">The host the target connects to.</param>
    /// <param name="remotePort">The port the target connects to.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">An argument is invalid.</exception>
    public IReadOnlyList<string> BuildLocalForward(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, int localPort, string remoteHost, int remotePort)
    {
        ArgumentNullException.ThrowIfNull(target);
        Guard.Port(localPort, nameof(localPort));
        Guard.Port(remotePort, nameof(remotePort));
        if (!SshEndpoint.IsValidHost(remoteHost))
        {
            throw new ArgumentException($"Remote host '{remoteHost}' is invalid.", nameof(remoteHost));
        }

        var forwardHost = remoteHost.Contains(':') ? $"[{remoteHost}]" : remoteHost;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return BuildCommonOptions()
            .Concat(new[]
            {
                "-N", "-n", "-T",
                "-o", "ExitOnForwardFailure=yes",
                "-o", "ServerAliveInterval=15",
                "-o", "ServerAliveCountMax=3",
                "-L", $"127.0.0.1:{localPort.ToString(invariant)}:{forwardHost}:{remotePort.ToString(invariant)}"
            })
            .Concat(ProxyJumpBuilder.Build(jumps))
            .Concat(new[] { "-p", target.Port.ToString(invariant), "--", target.Destination })
            .ToArray();
    }

    /// <summary>
    /// Builds the <c>GIT_SSH_COMMAND</c> value for git-over-ssh through the jump hosts.
    /// </summary>
    /// <param name="jumps">The jump hosts.</param>
    /// <returns>The command string. Git evaluates it through a shell, so every component is validated or single-quoted.</returns>
    /// <exception cref="ArgumentException">The executable or identity path contains a quote or control character.</exception>
    public string BuildGitSshCommand(IReadOnlyList<SshEndpoint> jumps)
    {
        var parts = new List<string> { ShellQuote(_options.SshExecutable) };
        foreach (var option in BuildCommonOptions())
        {
            parts.Add(option.StartsWith('-') ? option : ShellQuote(option));
        }

        parts.AddRange(ProxyJumpBuilder.Build(jumps));
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Wraps a value in single quotes for POSIX shells, rejecting values that cannot be quoted safely.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted value.</returns>
    /// <exception cref="ArgumentException">The value contains a quote or control character.</exception>
    internal static string ShellQuote(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\'') || value.Contains('"') || value.Any(char.IsControl))
        {
            throw new ArgumentException($"Value '{value}' cannot be used in GIT_SSH_COMMAND.", nameof(value));
        }

        return "'" + value.Replace('\\', '/') + "'";
    }
}
