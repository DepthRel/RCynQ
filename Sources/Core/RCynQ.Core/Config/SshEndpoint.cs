using System.Text.RegularExpressions;
using RCynQ.Core.Validation;
using RCynQ.Localization;

namespace RCynQ.Core.Config;

/// <summary>
/// An SSH destination: user, host and port.
/// </summary>
/// <remarks>
/// User and host are restricted to a conservative character set and may not start with
/// <c>-</c>. This prevents option injection into <c>ssh</c> command lines and makes the values
/// safe to embed in <c>GIT_SSH_COMMAND</c>, which Git evaluates through a shell.
/// </remarks>
public sealed partial record SshEndpoint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshEndpoint"/> record.
    /// </summary>
    /// <param name="user">The remote user name.</param>
    /// <param name="host">The host name, IPv4 address or IPv6 address (with or without brackets).</param>
    /// <param name="port">The SSH port. Defaults to <c>22</c>.</param>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The user or host is blank or contains forbidden characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is outside 1..65535.</exception>
    public SshEndpoint(string user, string host, int port = 22)
    {
        User = ValidateUser(user);
        Host = ValidateHost(host);
        Port = Guard.Port(port, nameof(port));
    }

    /// <summary>
    /// Gets the remote user name.
    /// </summary>
    public string User { get; }

    /// <summary>
    /// Gets the host name or address, without IPv6 brackets.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the SSH port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the <c>user@host</c> destination used as the positional <c>ssh</c> argument.
    /// </summary>
    public string Destination => $"{User}@{Host}";

    /// <summary>
    /// Gets the <c>user@host:port</c> form used inside <c>ssh -J</c> jump specifications.
    /// IPv6 hosts are wrapped in brackets.
    /// </summary>
    public string JumpSpec => Host.Contains(':') ? $"{User}@[{Host}]:{Port}" : $"{User}@{Host}:{Port}";

    /// <summary>
    /// Checks whether the given text is an acceptable SSH user name.
    /// </summary>
    /// <param name="user">The candidate user name.</param>
    /// <returns><see langword="true"/> when the value is acceptable; otherwise <see langword="false"/>.</returns>
    public static bool IsValidUser(string? user) => user is not null && UserPattern().IsMatch(user);

    /// <summary>
    /// Checks whether the given text is an acceptable host name or address.
    /// </summary>
    /// <param name="host">The candidate host.</param>
    /// <returns><see langword="true"/> when the value is acceptable; otherwise <see langword="false"/>.</returns>
    public static bool IsValidHost(string? host) => host is not null && HostPattern().IsMatch(host);

    /// <inheritdoc />
    public override string ToString() => JumpSpec;

    /// <summary>
    /// Validates the user name.
    /// </summary>
    /// <param name="user">The user name.</param>
    /// <returns>The trimmed user name.</returns>
    private static string ValidateUser(string? user)
    {
        var trimmed = Guard.NotBlank(user, nameof(user));
        if (!IsValidUser(trimmed))
        {
            throw new ArgumentException(
                Texts.Format("Ssh.UserForbidden", trimmed),
                nameof(user));
        }

        return trimmed;
    }

    /// <summary>
    /// Validates the host and strips IPv6 brackets.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The normalized host.</returns>
    private static string ValidateHost(string? host)
    {
        var trimmed = Guard.NotBlank(host, nameof(host));
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        if (!IsValidHost(trimmed))
        {
            throw new ArgumentException(
                Texts.Format("Ssh.HostForbidden", trimmed),
                nameof(host));
        }

        return trimmed;
    }

    /// <summary>
    /// Pattern for acceptable user names.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_.][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex UserPattern();

    /// <summary>
    /// Pattern for acceptable host names and addresses.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9.:][A-Za-z0-9.:-]{0,252}$")]
    private static partial Regex HostPattern();
}
