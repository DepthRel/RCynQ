using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Issues the tickets of this node: a random secret whose hash is stored, and, when the node takes enrollments on its SSH
/// server, a one-time SSH key whose private part leaves with the ticket and is never stored.
/// </summary>
public sealed class TicketIssuer
{
    /// <summary>The number of random bytes of a secret.</summary>
    private const int SECRET_BYTES = 32;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The tickets.</summary>
    private readonly IEnrollmentTicketRepository _tickets;

    /// <summary>The generator of the one-time keys.</summary>
    private readonly SshKeyGenerator _keys;

    /// <summary>The data folder, where a one-time key lives for the moment of its creation.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The command of the enrollment keys, or <see langword="null"/> when there is none.</summary>
    private readonly IEnrollmentCommandSource? _command;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _time;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TicketIssuer"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="options">The node options.</param>
    /// <param name="tickets">The tickets.</param>
    /// <param name="keys">The generator of the one-time keys.</param>
    /// <param name="appData">The data folder.</param>
    /// <param name="time">The clock.</param>
    /// <param name="command">The command of the enrollment keys, or <see langword="null"/> when there is none.</param>
    public TicketIssuer(
        ILocalizer localizer,
        SyncBridgeOptions options,
        IEnrollmentTicketRepository tickets,
        SshKeyGenerator keys,
        IAppDataFactory appData,
        TimeProvider time,
        IEnrollmentCommandSource? command = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _command = command;
    }

    /// <summary>
    /// Issues and stores a ticket. Its one-time key reaches <c>authorized_keys</c> with the next
    /// <see cref="Configuration.Access.PeerAccessService.ApplyAsync"/>.
    /// </summary>
    /// <param name="role">The role the enrolling node must have.</param>
    /// <param name="lifetime">How long the ticket is accepted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The ticket as it goes into an invitation.</returns>
    /// <exception cref="SyncBridgeException">
    /// The node takes enrollments on its SSH server but has no command line tool to run them, or the tool lies at a path
    /// that cannot go into <c>authorized_keys</c>.
    /// </exception>
    public async Task<InvitationTicket> IssueAsync(NodeRole role, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var sshServer = _options.Endpoint.Mode == EndpointMode.SshTunnel;
        if (sshServer)
        {
            EnsureCommandUsable();
        }

        var id = Guid.NewGuid();
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SECRET_BYTES));
        var now = _time.GetUtcNow();
        var expires = now + lifetime;

        var (privateKey, publicLine) = sshServer ? await CreateKeyAsync(id, cancellationToken) : (null, null);

        await _tickets.AddAsync(new EnrollmentTicket(id, role, Hash(secret), now, expires, publicLine), cancellationToken);
        return new InvitationTicket(id, secret, role, expires, privateKey);
    }

    /// <summary>
    /// Hashes a secret of a ticket the way it is stored.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <returns>The SHA-256 of its UTF-8 bytes.</returns>
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>
    /// Checks that the enrollment command exists and can be written into <c>authorized_keys</c>, before a ticket is
    /// stored: an open ticket whose line cannot be written would stop every later update of the file.
    /// </summary>
    /// <exception cref="SyncBridgeException">The command line tool is missing, or its path cannot be quoted safely.</exception>
    private void EnsureCommandUsable()
    {
        var command = _command?.GetCommand() ?? throw new SyncBridgeException(_localizer.Get("Invitation.NoCli"));

        try
        {
            AuthorizedKeysManager.EnsureEnrollmentCommandSafe(command, OperatingSystem.IsWindows());
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Invitation.UnsafeCli", string.Join(' ', command)), ex);
        }
    }

    /// <summary>
    /// Creates a one-time key in a folder of its own inside the data folder, reads both parts and removes the files.
    /// </summary>
    /// <param name="id">The ticket identifier, named in the comment of the key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The private key in OpenSSH format and the public key line.</returns>
    private async Task<(string PrivateKey, string PublicLine)> CreateKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_appData.Create().KeysDirectory, "enroll-" + id.ToString("N"));
        try
        {
            var pair = await _keys.GenerateAsync(Path.Combine(folder, "key"), $"{AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX}{id:N}", cancellationToken);
            return (await File.ReadAllTextAsync(pair.PrivateKeyPath, cancellationToken), pair.PublicKey.ToLine());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
