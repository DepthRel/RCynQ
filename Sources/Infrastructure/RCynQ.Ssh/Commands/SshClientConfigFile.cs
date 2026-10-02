using System.Text;
using RCynQ.Core.Config;
using RCynQ.Localization;

namespace RCynQ.Ssh.Commands;

/// <summary>
/// Writes the SSH client configuration RCynQ passes to every connection with <c>-F</c>.
/// </summary>
/// <remarks>
/// <para>
/// A chain is reached with <c>ssh -J</c>. The jump hosts of such a connection are contacted by further <c>ssh</c>
/// processes that receive none of the <c>-o</c> and <c>-i</c> options of the command line, only the configuration file.
/// Without this file a jump host would be asked with the keys and the <c>known_hosts</c> of the user profile, and a
/// question about an unknown host key would have nobody to answer it.
/// </para>
/// <para>
/// The file applies to every host and holds exactly the settings of <see cref="SshClientOptions"/>. Paths are written
/// in quotes and in full, so a path with spaces works here although it cannot be passed as an <c>-o</c> option.
/// </para>
/// <para>
/// The client hands the <c>-F</c> argument on to the process that contacts a jump host through a shell, without quoting
/// it: a full path with a space, or with anything else a shell reads, would never arrive there. <c>ssh</c> is therefore
/// started in the directory of the file and gets only its name (see <see cref="SshCommandBuilder.WorkingDirectory"/>),
/// which is why the name is restricted (see <see cref="SshClientOptions.IsValidConfigFileName"/>).
/// </para>
/// <para>
/// <c>UpdateHostKeys</c> is off: a pinned host is known by its pinned key and by no other (see
/// <c>PinnedHostKeyGuard</c>), and a client that adds further keys of a host by itself would widen that.
/// </para>
/// </remarks>
public sealed class SshClientConfigFile
{
    /// <summary>The SSH client options.</summary>
    private readonly SshClientOptions _options;

    /// <summary>Serializes the writes of this process.</summary>
    private readonly object _gate = new();

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshClientConfigFile"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="options">The SSH client options.</param>
    public SshClientConfigFile(ILocalizer localizer, SshClientOptions options)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Makes sure the configuration file holds the current settings.
    /// </summary>
    /// <returns>
    /// The full path of the file, or <see langword="null"/> when no file is configured; the connection then carries
    /// its settings on the command line only.
    /// </returns>
    /// <exception cref="ArgumentException">The name of the configured file cannot be handed on to a jump host.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public string? Ensure()
    {
        var configured = _options.ConfigFile;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (configured.Any(char.IsControl) || !SshClientOptions.IsValidConfigFileName(Path.GetFileName(configured)))
        {
            throw new ArgumentException(_localizer.Format("Config.SshConfigFileName", configured), nameof(SshClientOptions.ConfigFile));
        }

        var path = Path.GetFullPath(configured);
        var content = Render(_options);
        lock (_gate)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (!OperatingSystem.IsWindows())
                {
                    // ssh refuses a configuration file that others may write.
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        return path;
    }

    /// <summary>
    /// Renders the configuration for the given options.
    /// </summary>
    /// <param name="options">The SSH client options.</param>
    /// <returns>The text of the configuration file.</returns>
    public static string Render(SshClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = new StringBuilder();
        text.Append("# Written by RCynQ; changes made here are overwritten.\n");
        text.Append("# The settings apply to every host RCynQ connects to, also to the jump hosts of a chain.\n");
        text.Append("Host *\n");
        text.Append("    BatchMode yes\n");
        text.Append("    StrictHostKeyChecking accept-new\n");
        text.Append("    HashKnownHosts no\n");
        text.Append("    UpdateHostKeys no\n");
        text.Append("    ConnectTimeout ").Append(options.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

        // A hop that stopped answering is noticed on every hop, not only on the last one.
        text.Append("    ServerAliveInterval 15\n");
        text.Append("    ServerAliveCountMax 3\n");
        if (Quote(options.IdentityFile) is { } identity)
        {
            text.Append("    IdentityFile ").Append(identity).Append('\n');
            text.Append("    IdentitiesOnly yes\n");
        }

        if (Quote(options.KnownHostsFile) is { } knownHosts)
        {
            text.Append("    UserKnownHostsFile ").Append(knownHosts).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes a path the way a configuration file takes it: in quotes, with forward slashes, and with every percent sign
    /// doubled (a single one starts a token such as <c>%h</c>).
    /// </summary>
    /// <param name="path">The path, or <see langword="null"/>.</param>
    /// <returns>The quoted path, or <see langword="null"/> when there is none or it cannot be quoted.</returns>
    private static string? Quote(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => char.IsControl(c) || c == '"'))
        {
            return null;
        }

        return "\"" + Absolute(path.Trim()).Replace('\\', '/').Replace("%", "%%", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// Makes a path independent of the directory <c>ssh</c> is started in. A path that starts with <c>~</c> is left to
    /// the client, which reads it as the home directory.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static string Absolute(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.StartsWith('~') || Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }
}
