using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;

namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// Holds SSH connections to the host key fingerprints that were pinned when the peers were imported.
/// </summary>
/// <remarks>
/// The fingerprint that came with the export of a peer says which key that peer owns. The guard is called before a
/// connection is made: it makes the SSH client know every pinned host on the way by its pinned key and by no other.
/// From then on the client itself asks the host for that key and refuses any other, on every connection.
/// </remarks>
public interface IPinnedHostKeyGuard
{
    /// <summary>
    /// Makes sure the SSH client knows every pinned host of a chain by its pinned key only.
    /// </summary>
    /// <param name="hops">
    /// The peers an SSH session passes, in order: the hosts it jumps through, then the host it ends at. A host is
    /// asked for its key through the hosts before it.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every pinned host is known by its pinned key.</returns>
    /// <exception cref="SshExecutionException">
    /// A host owns no key with the pinned fingerprint, or presented no key at all. No key of that host stays trusted,
    /// and nothing was sent to it.
    /// </exception>
    Task EnsurePinnedAsync(IReadOnlyList<PeerDescriptor> hops, CancellationToken cancellationToken = default);
}
