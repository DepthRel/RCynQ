using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// Reads and updates the <c>authorized_keys</c> file of the local SSH server with restricted RCynQ keys.
/// </summary>
/// <remarks>
/// <para>
/// Lines are built from an allow-list of options. Forward-only keys also get a forced <c>command="exit 1"</c>:
/// <c>restrict</c> alone does not stop command execution, while <c>ssh -N</c> and <c>-J</c> never open a session, so
/// forwarding keeps working.
/// </para>
/// <para>
/// The file is rewritten through a temporary file in the same directory and moved into place; on Unix-like systems the
/// directory is <c>0700</c> and the file <c>0600</c>. The Windows <c>administrators_authorized_keys</c> file is restricted
/// to Administrators and SYSTEM, as OpenSSH requires.
/// </para>
/// </remarks>
public sealed partial class AuthorizedKeysManager
{
    /// <summary>
    /// The start of the comment of a one-time enrollment key; the ticket identifier follows. Lines with it are removed
    /// once their ticket is no longer open.
    /// </summary>
    public const string ENROLLMENT_COMMENT_PREFIX = "rcynq:enroll-";

    /// <summary>
    /// The characters <c>cmd.exe</c> expands even inside quotes (variables, delayed expansion): a word of the enrollment
    /// command with one of them is refused.
    /// </summary>
    private static readonly char[] _cmdVariableCharacters = { '%', '!' };

    /// <summary>
    /// The characters <c>cmd.exe</c> acts on outside quotes (command separators, redirections, its escape): a word of the
    /// enrollment command with one of them is put in quotes.
    /// </summary>
    private static readonly char[] _cmdSpecialCharacters = { '&', '|', '<', '>', '^', '(', ')' };

    /// <summary>The file this manager reads and writes.</summary>
    private readonly AuthorizedKeysTarget _target;

    /// <summary>Checks whether the process runs with administrator rights.</summary>
    private readonly Func<bool> _isElevated;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizedKeysManager"/> class for the file the local SSH server reads.
    /// </summary>
    /// <param name="options">The SSH options; <see cref="SshClientOptions.AuthorizedKeysPath"/> overrides the detected file.</param>
    public AuthorizedKeysManager(SshClientOptions options)
        : this(AuthorizedKeysLocation.Detect(options), WindowsAccounts.IsElevated)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizedKeysManager"/> class for an explicit file.
    /// </summary>
    /// <param name="target">The file.</param>
    /// <param name="isElevated">Checks whether the process runs with administrator rights.</param>
    public AuthorizedKeysManager(AuthorizedKeysTarget target, Func<bool> isElevated)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
    }

    /// <summary>
    /// Gets the managed file path.
    /// </summary>
    public string FilePath => _target.FilePath;

    /// <summary>
    /// Gets a value indicating whether the file is the Windows file for members of the Administrators group.
    /// </summary>
    public bool IsAdministratorsFile => _target.IsAdministratorsFile;

    /// <summary>
    /// Gets a value indicating whether this process may write the file (the administrators file needs elevation).
    /// </summary>
    public bool CanWrite => !_target.IsAdministratorsFile || _isElevated();

    /// <summary>
    /// Builds a restricted <c>authorized_keys</c> line.
    /// </summary>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="key">The public key.</param>
    /// <param name="target">
    /// For <see cref="AuthorizedKeyPurpose.GitReadOnly"/> the absolute repository path; otherwise the
    /// <c>host:port</c> the key may open.
    /// </param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">The target is unsafe or has the wrong form.</exception>
    public static string BuildLine(AuthorizedKeyPurpose purpose, SshPublicKey key, string target)
    {
        ArgumentNullException.ThrowIfNull(key);
        var options = purpose switch
        {
            AuthorizedKeyPurpose.GitReadOnly =>
                $"restrict,command=\"git-upload-pack '{ValidateRepoPath(target)}'\",no-pty,no-agent-forwarding,no-port-forwarding,no-x11-forwarding",
            AuthorizedKeyPurpose.PatchForward or AuthorizedKeyPurpose.RelayForward =>
                $"restrict,port-forwarding,permitopen=\"{ValidatePermitOpen(target)}\",command=\"exit 1\",no-pty,no-agent-forwarding,no-x11-forwarding",
            _ => throw new ArgumentException($"Unknown key purpose {purpose}.", nameof(purpose))
        };

        var comment = $"rcynq:{purpose}";
        return $"{options} {key.Type} {key.Blob} {comment}";
    }

    /// <summary>
    /// Builds a restricted line that may only open the listed <c>host:port</c> targets and, optionally, run
    /// <c>git-upload-pack</c> for one repository.
    /// </summary>
    /// <param name="key">The public key.</param>
    /// <param name="targets">The <c>host:port</c> targets (IPv6 hosts in brackets).</param>
    /// <param name="gitRepoPath">The repository for a forced <c>git-upload-pack</c>, or <see langword="null"/> for <c>exit 1</c>.</param>
    /// <param name="comment">The line comment, for example <c>rcynq:workstation-1</c>.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">A target, the repository path or the comment is unsafe.</exception>
    public static string BuildForwardLine(SshPublicKey key, IReadOnlyList<string> targets, string? gitRepoPath, string comment)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one forward target is required.", nameof(targets));
        }

        if (string.IsNullOrWhiteSpace(comment) || comment.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\''))
        {
            throw new ArgumentException("The comment must be a single word without quotes.", nameof(comment));
        }

        var permits = string.Join(',', targets.Select(target => $"permitopen=\"{ValidatePermitOpen(target)}\""));
        var command = gitRepoPath is null ? "exit 1" : $"git-upload-pack '{ValidateRepoPath(gitRepoPath)}'";
        return $"restrict,port-forwarding,{permits},command=\"{command}\",no-pty,no-agent-forwarding,no-x11-forwarding {key.Type} {key.Blob} {comment}";
    }

    /// <summary>
    /// Builds the line of a one-time enrollment key: it may only run the enrollment command, opens no terminal and no
    /// forward, and the SSH server refuses it after its expiry even if the program never removes it.
    /// </summary>
    /// <param name="key">The public part of the one-time key.</param>
    /// <param name="command">The words of the enrollment command: the program, its arguments and the paths it needs.</param>
    /// <param name="windowsShell">Whether sshd runs the command with <c>cmd.exe</c> (Windows) rather than a POSIX shell.</param>
    /// <param name="expiresLocal">The expiry in the local time of this computer, as <c>expiry-time</c> reads it.</param>
    /// <param name="ticketId">The ticket identifier, kept in the comment.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">A word of the command cannot be quoted safely.</exception>
    public static string BuildEnrollmentLine(SshPublicKey key, IReadOnlyList<string> command, bool windowsShell, DateTime expiresLocal, Guid ticketId)
    {
        ArgumentNullException.ThrowIfNull(key);
        EnsureEnrollmentCommandSafe(command, windowsShell);

        string shellCommand;
        if (windowsShell)
        {
            // The OpenSSH server of Windows puts the command in quotes itself before cmd.exe /c, which drops that outer
            // pair again; a word with a space only needs its own quotes (checked against a running sshd). A word with a
            // character cmd.exe acts on gets them as well, so the character stays part of the path.
            shellCommand = string.Join(' ', command.Select(word => word.Any(char.IsWhiteSpace) || word.IndexOfAny(_cmdSpecialCharacters) >= 0 ? $"\"{word}\"" : word));
        }
        else
        {
            shellCommand = string.Join(' ', command.Select(word => $"'{word}'"));
        }

        var expiry = expiresLocal.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
        var quoted = shellCommand.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"restrict,expiry-time=\"{expiry}\",command=\"{quoted}\" {key.Type} {key.Blob} {ENROLLMENT_COMMENT_PREFIX}{ticketId:N}";
    }

    /// <summary>
    /// Checks that the words of an enrollment command can go into a line of <c>authorized_keys</c>: none may break out of
    /// its quoting, here or in the shell sshd runs it with (<c>cmd.exe</c> on Windows, the login shell elsewhere).
    /// </summary>
    /// <param name="command">The words of the enrollment command.</param>
    /// <param name="windowsShell">Whether sshd runs the command with <c>cmd.exe</c> (Windows) rather than a POSIX shell.</param>
    /// <exception cref="ArgumentException">The command is empty, or a word of it cannot be quoted safely.</exception>
    public static void EnsureEnrollmentCommandSafe(IReadOnlyList<string> command, bool windowsShell)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("The enrollment command is empty.", nameof(command));
        }

        foreach (var word in command)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Any(char.IsControl) || word.Contains('"') || (windowsShell ? word.IndexOfAny(_cmdVariableCharacters) >= 0 : word.Contains('\'')))
            {
                throw new ArgumentException($"The enrollment command word '{word}' cannot be quoted safely.", nameof(command));
            }
        }
    }

    /// <summary>
    /// Removes the lines of one-time enrollment keys except the given ones; other lines stay as they are.
    /// </summary>
    /// <param name="keepBlobs">The key blobs of the enrollment lines that stay (the open tickets).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of lines removed.</returns>
    public async Task<int> RemoveEnrollmentLinesAsync(IReadOnlySet<string> keepBlobs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keepBlobs);
        if (!File.Exists(FilePath))
        {
            return 0;
        }

        var existing = await File.ReadAllLinesAsync(FilePath, cancellationToken);
        var kept = existing.Where(line => !IsStaleEnrollmentLine(line.Trim(), keepBlobs)).ToArray();
        if (kept.Length == existing.Length)
        {
            return 0;
        }

        await WriteLinesAsync(kept, cancellationToken);
        return existing.Length - kept.Length;
    }

    /// <summary>
    /// Tells whether a line is the line of an enrollment key that is not kept.
    /// </summary>
    /// <param name="line">The trimmed line.</param>
    /// <param name="keepBlobs">The key blobs that stay.</param>
    /// <returns><see langword="true"/> when the line goes.</returns>
    private static bool IsStaleEnrollmentLine(string line, IReadOnlySet<string> keepBlobs)
    {
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return false;
        }

        try
        {
            var entry = ParseLine(line, 0);
            return entry.Key.Comment is { } comment &&
                comment.StartsWith(ENROLLMENT_COMMENT_PREFIX, StringComparison.Ordinal) &&
                !keepBlobs.Contains(entry.Key.Blob);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the line for a peer according to its role: Workstation peers get Git and patch access on Origin,
    /// Origin peers get patch access on Workstation, and every peer gets forward access on a Relay.
    /// </summary>
    /// <param name="localRole">The local node role.</param>
    /// <param name="key">The peer public key.</param>
    /// <param name="target">The repository path or <c>host:port</c> target.</param>
    /// <param name="purpose">The requested purpose.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">The purpose is not allowed for the local role.</exception>
    public static string BuildLineForRole(NodeRole localRole, SshPublicKey key, string target, AuthorizedKeyPurpose purpose)
    {
        var allowed = localRole switch
        {
            NodeRole.Relay => purpose == AuthorizedKeyPurpose.RelayForward,
            NodeRole.Origin => purpose is AuthorizedKeyPurpose.GitReadOnly or AuthorizedKeyPurpose.PatchForward,
            NodeRole.Workstation => purpose == AuthorizedKeyPurpose.PatchForward,
            _ => false
        };

        if (!allowed)
        {
            throw new ArgumentException($"Key purpose {purpose} is not allowed on a {localRole} node.", nameof(purpose));
        }

        return BuildLine(purpose, key, target);
    }

    /// <summary>
    /// Reads and parses the file.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The parsed file; empty when it does not exist.</returns>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read without administrator rights.</exception>
    public async Task<AuthorizedKeysFile> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new AuthorizedKeysFile(Array.Empty<AuthorizedKeyEntry>(), Array.Empty<int>());
        }

        return Parse(await File.ReadAllLinesAsync(FilePath, cancellationToken));
    }

    /// <summary>
    /// Adds a restricted key unless a line with the same key blob exists.
    /// </summary>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="key">The public key.</param>
    /// <param name="target">The repository path or <c>host:port</c> target.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the line was added; <see langword="false"/> when the key was already present.</returns>
    /// <exception cref="ArgumentException">The target is unsafe.</exception>
    public async Task<bool> AddAsync(AuthorizedKeyPurpose purpose, SshPublicKey key, string target, CancellationToken cancellationToken = default)
    {
        var line = BuildLine(purpose, key, target);
        var existing = File.Exists(FilePath) ? await File.ReadAllLinesAsync(FilePath, cancellationToken) : Array.Empty<string>();
        if (Parse(existing).Entries.Any(entry => entry.Key.Blob == key.Blob))
        {
            return false;
        }

        await WriteLinesAsync(existing.Append(line).ToArray(), cancellationToken);
        return true;
    }

    /// <summary>
    /// Writes restricted lines, replacing the existing lines of the same keys and keeping every other line.
    /// </summary>
    /// <param name="lines">The complete lines to have in the file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of lines that were added or changed; 0 when the file already matched and was not rewritten.</returns>
    /// <exception cref="FormatException">A line is not a valid <c>authorized_keys</c> line.</exception>
    public async Task<int> UpsertAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var desired = lines.Select(line => line.Trim()).ToArray();
        var blobs = desired.Select(line => ParseLine(line, 0).Key.Blob).ToHashSet(StringComparer.Ordinal);

        var existing = File.Exists(FilePath) ? await File.ReadAllLinesAsync(FilePath, cancellationToken) : Array.Empty<string>();
        var kept = new List<string>();
        var replaced = new List<string>();
        foreach (var line in existing)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#') && TryGetBlob(trimmed) is { } blob && blobs.Contains(blob))
            {
                replaced.Add(trimmed);
                continue;
            }

            kept.Add(line);
        }

        var changed = desired.Count(line => !replaced.Contains(line, StringComparer.Ordinal));
        if (changed == 0 && replaced.Count == desired.Length)
        {
            return 0;
        }

        kept.AddRange(desired);
        await WriteLinesAsync(kept, cancellationToken);
        return changed;
    }

    /// <summary>
    /// Parses <c>authorized_keys</c> lines.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The parsed file.</returns>
    public static AuthorizedKeysFile Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var entries = new List<AuthorizedKeyEntry>();
        var invalid = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            try
            {
                entries.Add(ParseLine(line, i + 1));
            }
            catch (FormatException)
            {
                invalid.Add(i + 1);
            }
        }

        return new AuthorizedKeysFile(entries, invalid);
    }

    /// <summary>
    /// Parses one non-empty line, splitting the options field while honoring quotes.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="lineNumber">The line number.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="FormatException">The line is malformed.</exception>
    private static AuthorizedKeyEntry ParseLine(string line, int lineNumber)
    {
        var tokens = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw LocalizedError.Format("Keys.LineEmpty", lineNumber);
        }

        if (SshPublicKey.AllowedTypes.Contains(tokens[0]))
        {
            return new AuthorizedKeyEntry(null, SshPublicKey.Parse(line), lineNumber);
        }

        var inQuotes = false;
        var end = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && char.IsWhiteSpace(line[i]))
            {
                end = i;
                break;
            }
        }

        if (end < 0 || inQuotes)
        {
            throw LocalizedError.Format("Keys.LineNoKey", lineNumber);
        }

        return new AuthorizedKeyEntry(line[..end], SshPublicKey.Parse(line[end..]), lineNumber);
    }

    /// <summary>
    /// Gets the key blob of a line.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The blob, or <see langword="null"/> for a line that cannot be parsed.</returns>
    private static string? TryGetBlob(string line)
    {
        try
        {
            return ParseLine(line, 0).Key.Blob;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates a repository path for a forced <c>git-upload-pack</c> command.
    /// </summary>
    /// <param name="target">The path.</param>
    /// <returns>The path with <c>/</c> separators.</returns>
    /// <exception cref="ArgumentException">The path is not absolute or contains unsafe characters.</exception>
    private static string ValidateRepoPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !SafeRepoPath().IsMatch(target) || !(target.StartsWith('/') || Path.IsPathFullyQualified(target)))
        {
            throw new ArgumentException($"Repository path '{target}' must be absolute and must not contain quotes, spaces or shell metacharacters.", nameof(target));
        }

        return target.Replace('\\', '/');
    }

    /// <summary>
    /// Validates a <c>host:port</c> permitopen target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentException">The target is malformed.</exception>
    private static string ValidatePermitOpen(string target)
    {
        var separator = target?.LastIndexOf(':') ?? -1;
        if (separator <= 0 ||
            !int.TryParse(target![(separator + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new ArgumentException($"Forward target '{target}' must have the form host:port.", nameof(target));
        }

        var host = target[..separator].Trim('[', ']');
        if (!SshEndpoint.IsValidHost(host))
        {
            throw new ArgumentException($"Forward target host '{host}' is invalid.", nameof(target));
        }

        return target;
    }

    /// <summary>
    /// Restricts the Windows administrators file to Administrators and SYSTEM.
    /// </summary>
    /// <param name="path">The file.</param>
    [SupportedOSPlatform("windows")]
    private static void ProtectAdministratorsFile(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    /// <summary>
    /// Writes the file atomically with the permissions OpenSSH expects.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var content = new StringBuilder();
        foreach (var line in lines)
        {
            content.Append(line).Append('\n');
        }

        var temp = Path.Combine(directory, $".authorized_keys.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, content.ToString(), new UTF8Encoding(false), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, FilePath, overwrite: true);
            if (OperatingSystem.IsWindows() && IsAdministratorsFile)
            {
                ProtectAdministratorsFile(FilePath);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Pattern for repository paths inside forced commands.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9_./:\\~+-]+$")]
    private static partial Regex SafeRepoPath();
}
