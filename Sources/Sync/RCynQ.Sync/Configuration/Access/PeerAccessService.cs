using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// Allows the imported neighbours to connect to this node by writing restricted lines into the <c>authorized_keys</c>
/// file of the local SSH server, together with the one-time keys of the open invitations; the lines of closed
/// invitations are removed.
/// </summary>
public sealed class PeerAccessService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The link repository, whose routes name the other addresses of the neighbours; <see langword="null"/> when unknown.</summary>
    private readonly IChainLinkRepository? _links;

    /// <summary>The authorized keys file.</summary>
    private readonly AuthorizedKeysManager _keys;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The tickets of the invitations, or <see langword="null"/> when the node writes no enrollment keys.</summary>
    private readonly IEnrollmentTicketRepository? _tickets;

    /// <summary>The command of the enrollment keys, or <see langword="null"/> when the node writes none.</summary>
    private readonly IEnrollmentCommandSource? _enrollment;

    /// <summary>The clock that decides which tickets are open and gives the local time zone.</summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerAccessService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="keys">The authorized keys file.</param>
    /// <param name="links">The link repository, whose routes name the other addresses of the neighbours; <see langword="null"/> uses the one address of each neighbour.</param>
    /// <param name="tickets">The tickets of the invitations; <see langword="null"/> writes no enrollment keys.</param>
    /// <param name="enrollment">The command of the enrollment keys; <see langword="null"/> writes no enrollment keys.</param>
    /// <param name="time">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public PeerAccessService(
        ILocalizer localizer,
        NodeConfig node,
        SyncBridgeOptions options,
        IPeerRepository peers,
        AuthorizedKeysManager keys,
        IChainLinkRepository? links = null,
        IEnrollmentTicketRepository? tickets = null,
        IEnrollmentCommandSource? enrollment = null,
        TimeProvider? time = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _links = links;
        _tickets = tickets;
        _enrollment = enrollment;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Writes the lines for all neighbours that need access and for the open invitations; lines of other keys are kept,
    /// lines of closed invitations are removed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; when administrator rights are needed nothing is written.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is not writable.</exception>
    /// <exception cref="ArgumentException">The enrollment command cannot be written into a line safely.</exception>
    public async Task<PeerAccessResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        // Neighbours of a node without an SSH server connect to its TLS port; no SSH server reads authorized_keys here.
        if (_options.Endpoint.Mode == EndpointMode.Direct)
        {
            return new PeerAccessResult(
                false, false, _keys.FilePath, 0, Array.Empty<string>(), Array.Empty<string>(),
                _localizer.Get("Access.NoSshServer"));
        }

        var plan = PeerAccessPlanner.Plan(
            _node,
            _options.ListenPort,
            await _peers.GetAllAsync(cancellationToken),
            _links is null ? null : await _links.GetAllAsync(cancellationToken));
        var missing = plan.MissingKeys.Select(peer => peer.Id.Value).ToArray();
        var waiting = plan.WaitingForCounterpart.Select(peer => peer.Id.Value).ToArray();
        var enrollment = await EnrollmentLinesAsync(cancellationToken);
        var lines = plan.Entries.Select(entry => entry.Line).Concat(enrollment.Select(item => item.Line)).ToArray();
        var keep = enrollment.Select(item => item.Blob).ToHashSet(StringComparer.Ordinal);

        if (lines.Length == 0)
        {
            // The one-time keys of closed invitations still go; the SSH server refuses them after their expiry anyway.
            if (_keys.CanWrite)
            {
                await _keys.RemoveEnrollmentLinesAsync(keep, cancellationToken);
            }

            return new PeerAccessResult(false, false, _keys.FilePath, 0, missing, waiting, _localizer.Get("Access.NobodyNeedsAccess"));
        }

        if (!_keys.CanWrite)
        {
            return new PeerAccessResult(false, true, _keys.FilePath, lines.Length, missing, waiting,
                _localizer.Format("Access.NeedsAdmin", _keys.FilePath));
        }

        var changed = await _keys.UpsertAsync(lines, cancellationToken);
        changed += await _keys.RemoveEnrollmentLinesAsync(keep, cancellationToken);
        return new PeerAccessResult(true, false, _keys.FilePath, lines.Length, missing, waiting,
            _localizer.Format("Access.Allowed", lines.Length, _keys.FilePath, changed));
    }

    /// <summary>
    /// Builds the lines of the one-time keys of the open invitations.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The lines with the blobs of their keys; empty when the node writes no enrollment keys.</returns>
    private async Task<IReadOnlyList<(string Line, string Blob)>> EnrollmentLinesAsync(CancellationToken cancellationToken)
    {
        var command = _enrollment?.GetCommand();
        if (_tickets is null || command is null)
        {
            return Array.Empty<(string, string)>();
        }

        var now = _time.GetUtcNow();
        var open = (await _tickets.GetAllAsync(cancellationToken)).Where(ticket => ticket.IsOpen(now) && ticket.EnrollmentKey is not null);
        return open
            .Select(ticket =>
            {
                var key = SshPublicKey.Parse(ticket.EnrollmentKey!);
                var expires = TimeZoneInfo.ConvertTime(ticket.ExpiresAtUtc, _time.LocalTimeZone).DateTime;
                return (AuthorizedKeysManager.BuildEnrollmentLine(key, command, OperatingSystem.IsWindows(), expires, ticket.Id), key.Blob);
            })
            .ToArray();
    }
}
