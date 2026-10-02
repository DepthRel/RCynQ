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
/// <para>
/// The same settings are also written to a configuration file passed with <c>-F</c> (see
/// <see cref="SshClientConfigFile"/>): the options of the command line reach only the destination, the file reaches the
/// jump hosts of a chain too.
/// </para>
/// </remarks>
public sealed class SshCommandBuilder
{
    /// <summary>
    /// The SSH client options.
    /// </summary>
    private readonly SshClientOptions _options;

    /// <summary>
    /// The configuration file passed to every connection, or <see langword="null"/>.
    /// </summary>
    private readonly SshClientConfigFile? _configFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshCommandBuilder"/> class.
    /// </summary>
    /// <param name="options">The SSH client options.</param>
    /// <param name="configFile">
    /// The configuration file passed to every connection, or <see langword="null"/> to carry the settings on the command
    /// line only (they then do not reach jump hosts).
    /// </param>
    public SshCommandBuilder(SshClientOptions options, SshClientConfigFile? configFile = null)
    {
        _options = options;
        _configFile = configFile;
    }

    /// <summary>
    /// Gets the <c>ssh</c> executable.
    /// </summary>
    public string Executable => _options.SshExecutable;

    /// <summary>
    /// Gets the directory <c>ssh</c> has to be started in: the one that holds the configuration file, which the
    /// arguments name without a directory. <see langword="null"/> when there is no configuration file, and any
    /// directory will do.
    /// </summary>
    /// <remarks>
    /// The client hands the <c>-F</c> argument on to the process that contacts a jump host through a shell and does not
    /// quote it. A bare file name survives that; a full path with a space does not. The process that contacts the jump
    /// host is started in the same directory, so the name leads it to the same file.
    /// </remarks>
    /// <exception cref="ArgumentException">The name of the configured file cannot be handed on to a jump host.</exception>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    public string? WorkingDirectory => _configFile?.Ensure() is { } config ? Path.GetDirectoryName(config) : null;

    /// <summary>
    /// Builds the options shared by every connection. They are meant for an <c>ssh</c> started in
    /// <see cref="WorkingDirectory"/>.
    /// </summary>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">The name of the configured file cannot be handed on to a jump host.</exception>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    public IReadOnlyList<string> BuildCommonOptions()
    {
        var args = new List<string>();
        var config = _configFile?.Ensure();
        if (config is not null)
        {
            args.AddRange(new[] { "-F", Path.GetFileName(config) });
        }

        args.AddRange(new[]
        {
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", $"ConnectTimeout={_options.ConnectTimeoutSeconds}"
        });

        // With a configuration file ssh runs in the directory of that file, so a relative path would point elsewhere.
        if (!string.IsNullOrWhiteSpace(_options.IdentityFile))
        {
            var identity = config is null ? _options.IdentityFile : SshClientConfigFile.Absolute(_options.IdentityFile);
            args.AddRange(new[] { "-i", identity, "-o", "IdentitiesOnly=yes" });
        }

        var knownHostsFile = config is null || string.IsNullOrWhiteSpace(_options.KnownHostsFile)
            ? _options.KnownHostsFile
            : SshClientConfigFile.Absolute(_options.KnownHostsFile);
        if (KnownHostsOption(knownHostsFile) is { } knownHosts)
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
    /// Builds the connection that asks a host for its host key of one kind and logs in nowhere.
    /// </summary>
    /// <param name="target">The host to ask.</param>
    /// <param name="jumps">The jump hosts the question passes.</param>
    /// <param name="hostKeyAlgorithms">The kinds of key to ask for, as a <c>HostKeyAlgorithms</c> value.</param>
    /// <param name="knownHostsFileName">
    /// The name of the file, in the directory <c>ssh</c> is started in, that receives the key the host presents.
    /// </param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <para>
    /// The client records the key a host presents during the key exchange, before anybody logs in, so the connection
    /// offers no way to log in (<c>PreferredAuthentications=none</c>) and ends there: the key is learned and nothing is
    /// sent. The file of its own keeps the answer apart from the keys the node trusts, and the file of the system is
    /// left out, so a key recorded there cannot stand in for the one the host presents now.
    /// </para>
    /// <para>
    /// The options of the question come first: <c>ssh</c> takes the first value given for an option. The jump hosts are
    /// reached with the settings of the configuration file, the trusted keys of the node included.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">An argument is invalid.</exception>
    public IReadOnlyList<string> BuildHostKeyProbe(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string hostKeyAlgorithms, string knownHostsFileName)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrEmpty(hostKeyAlgorithms) || !hostKeyAlgorithms.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or ',' or '@' or '.'))
        {
            throw new ArgumentException($"Host key algorithms '{hostKeyAlgorithms}' are invalid.", nameof(hostKeyAlgorithms));
        }

        if (!SshClientOptions.IsValidConfigFileName(knownHostsFileName))
        {
            throw new ArgumentException($"File name '{knownHostsFileName}' is invalid.", nameof(knownHostsFileName));
        }

        var args = new List<string>();
        if (_configFile?.Ensure() is { } config)
        {
            args.AddRange(new[] { "-F", Path.GetFileName(config) });
        }

        args.AddRange(new[]
        {
            "-o", "UserKnownHostsFile=" + knownHostsFileName,
            "-o", "GlobalKnownHostsFile=" + knownHostsFileName + ".global",
            "-o", "HostKeyAlgorithms=" + hostKeyAlgorithms,
            "-o", "PreferredAuthentications=none",
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "HashKnownHosts=no",
            "-o", "UpdateHostKeys=no",
            "-o", $"ConnectTimeout={_options.ConnectTimeoutSeconds}",
            "-T", "-n"
        });
        args.AddRange(ProxyJumpBuilder.Build(jumps));
        args.AddRange(new[] { "-p", target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--", target.Destination, "true" });
        return args;
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
    /// <returns>
    /// The command string. Git evaluates it through a shell, so every component is validated or single-quoted. With a
    /// configuration file the command first changes to <see cref="WorkingDirectory"/>: Git starts <c>ssh</c> in the
    /// working tree, where the name of the file would lead nowhere.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The executable, the identity path or the directory of the configuration file contains a quote or control character.
    /// </exception>
    public string BuildGitSshCommand(IReadOnlyList<SshEndpoint> jumps)
    {
        var parts = new List<string>();
        if (WorkingDirectory is { } directory)
        {
            parts.AddRange(new[] { "cd", ShellQuote(directory), "&&" });
        }

        parts.Add(ShellQuote(_options.SshExecutable));
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
