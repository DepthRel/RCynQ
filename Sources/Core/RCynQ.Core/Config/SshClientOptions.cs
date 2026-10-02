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
    /// Gets or sets the file RCynQ writes its SSH client configuration to and passes to every connection with
    /// <c>-F</c>. When empty, the service and the CLI use <c>keys/ssh_config</c> in the data directory.
    /// </summary>
    /// <remarks>
    /// Options given on the command line reach only the destination of a connection, not the jump hosts of a chain: a
    /// jump host is contacted by a second <c>ssh</c> that inherits nothing but the configuration file. The identity
    /// file, the <c>known_hosts</c> file and the non-interactive mode are therefore written to this file, so every hop
    /// uses them. The file is rewritten when the settings change; the configuration in the user profile is not read.
    /// <para>
    /// The directory may be any, but the name of the file is restricted (see <see cref="IsValidConfigFileName"/>):
    /// <c>ssh</c> is started in that directory and gets only the name, because it hands the argument on to the process
    /// that contacts a jump host through a shell, without quoting it.
    /// </para>
    /// </remarks>
    public string? ConfigFile { get; set; }

    /// <summary>
    /// Tells whether a file name can serve as the name of <see cref="ConfigFile"/>: letters, digits, <c>.</c>, <c>_</c>
    /// and <c>-</c>, not starting with <c>-</c>.
    /// </summary>
    /// <param name="fileName">The file name, without a directory.</param>
    /// <returns><see langword="true"/> when the name passes unquoted through a shell unchanged.</returns>
    public static bool IsValidConfigFileName(string? fileName)
        => !string.IsNullOrEmpty(fileName)
            && fileName[0] != '-'
            && fileName is not ("." or "..")
            && fileName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// Gets or sets the <c>git</c> executable name or path.
    /// </summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Gets or sets the port of the local SSH server checked by diagnostics.
    /// </summary>
    public int LocalSshdPort { get; set; } = 22;
}
