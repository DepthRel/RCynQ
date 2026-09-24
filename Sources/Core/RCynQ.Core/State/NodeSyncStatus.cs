using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;

namespace RCynQ.Core.State;

/// <summary>
/// The synchronization status of the local node as shown by the UI and CLI.
/// </summary>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Name">The node display name.</param>
/// <param name="Role">The node role.</param>
/// <param name="State">The persisted synchronization state.</param>
/// <param name="SyncBase">The sync base of the node and whether it is ready.</param>
/// <param name="PendingChanges">The local changes the peer does not have yet.</param>
/// <param name="ConflictPendingCount">The number of incoming patches waiting for conflict resolution.</param>
/// <param name="ChangedFiles">
/// Every file that differs from the Git commit, including those the peer already has; <see langword="null"/> when the service is of
/// an older version that reports only <paramref name="PendingChanges"/>.
/// </param>
public sealed record NodeSyncStatus(
    NodeId NodeId,
    string Name,
    NodeRole Role,
    SyncState State,
    SyncBaseStatus SyncBase,
    IReadOnlyList<FileChange> PendingChanges,
    int ConflictPendingCount,
    IReadOnlyList<ChangedFile>? ChangedFiles = null);
