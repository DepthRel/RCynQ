using RCynQ.Core.Enums;

namespace RCynQ.Core.Config;

/// <summary>
/// Bindable SSH client settings.
/// </summary>
public sealed class SshClientOptions
{
    /// <summary>
    /// Gets or sets the <c>ssh</c> executable name or path.
    /// </summary>
    public string SshExecutable { get; set; } = "ssh";

    /// <summary>
    /// Gets or sets the <c>ssh-keygen</c> executable name or path.
    /// </summary>
    public string SshKeygenExecutable { get; set; } = "ssh-keygen";

    /// <summary>
    /// Gets or sets the <c>ssh-keyscan</c> executable name or path.
    /// </summary>
    public string SshKeyscanExecutable { get; set; } = "ssh-keyscan";

    /// <summary>
    /// Gets or sets the private key file passed with <c>-i</c>, or <see langword="null"/> to use SSH defaults.
    /// </summary>
    public string? IdentityFile { get; set; }

    /// <summary>
    /// Gets or sets the SSH connect timeout in seconds.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the path of the <c>authorized_keys</c> file managed on this node, or <see langword="null"/>
    /// for <c>~/.ssh/authorized_keys</c>.
    /// </summary>
    public string? AuthorizedKeysPath { get; set; }

    /// <summary>
    /// Gets or sets the <c>known_hosts</c> file of RCynQ connections. When empty, the service and the CLI use
    /// <c>keys/known_hosts</c> in the data directory, so the user's own <c>~/.ssh/known_hosts</c> is not touched.
    /// </summary>
    public string? KnownHostsFile { get; set; }

    /// <summary>
    /// Gets or sets the <c>git</c> executable name or path.
    /// </summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Gets or sets the port of the local SSH server checked by diagnostics.
    /// </summary>
    public int LocalSshdPort { get; set; } = 22;
}
