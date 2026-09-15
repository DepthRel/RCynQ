using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// Allows the imported neighbours to connect to this node by writing restricted lines into the <c>authorized_keys</c>
/// file of the local SSH server.
/// </summary>
public sealed class PeerAccessService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The authorized keys file.</summary>
    private readonly AuthorizedKeysManager _keys;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerAccessService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="keys">The authorized keys file.</param>
    public PeerAccessService(NodeConfig node, SyncBridgeOptions options, IPeerRepository peers, AuthorizedKeysManager keys)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <summary>
    /// Writes the lines for all neighbours that need access; lines of other keys are kept.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; when administrator rights are needed nothing is written.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is not writable.</exception>
    public async Task<PeerAccessResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var plan = PeerAccessPlanner.Plan(_node, _options.ListenPort, await _peers.GetAllAsync(cancellationToken), allowGitCommand: !OperatingSystem.IsWindows());
        var missing = plan.MissingKeys.Select(peer => peer.Id.Value).ToArray();
        var waiting = plan.WaitingForCounterpart.Select(peer => peer.Id.Value).ToArray();

        if (plan.Entries.Count == 0)
        {
            return new PeerAccessResult(false, false, _keys.FilePath, 0, missing, waiting, "No neighbour needs SSH access to this node yet.");
        }

        if (!_keys.CanWrite)
        {
            return new PeerAccessResult(false, true, _keys.FilePath, plan.Entries.Count, missing, waiting,
                $"Administrator rights are required to update '{_keys.FilePath}'. Run 'rcynq ssh allow' as administrator.");
        }

        var changed = await _keys.UpsertAsync(plan.Entries.Select(entry => entry.Line).ToArray(), cancellationToken);
        return new PeerAccessResult(true, false, _keys.FilePath, plan.Entries.Count, missing, waiting,
            $"Allowed {plan.Entries.Count} neighbour key(s) in '{_keys.FilePath}' ({changed} line(s) changed).");
    }
}
