using RCynQ.Core.State;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists the synchronization state of a node.
/// </summary>
public interface INodeStateStore
{
    /// <summary>
    /// Gets the current synchronization state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the current state, or an empty state when no
    /// state has been recorded yet.
    /// </returns>
    Task<SyncState> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the given synchronization state.
    /// </summary>
    /// <param name="state">The state to save. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);
}
