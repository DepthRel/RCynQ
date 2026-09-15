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
/// <param name="BaselineInitialized">Whether the sync base snapshot exists.</param>
/// <param name="PendingChanges">The local changes not yet synchronized.</param>
/// <param name="ConflictPendingCount">The number of incoming patches waiting for conflict resolution.</param>
public sealed record NodeSyncStatus(
    NodeId NodeId,
    string Name,
    NodeRole Role,
    SyncState State,
    bool BaselineInitialized,
    IReadOnlyList<FileChange> PendingChanges,
    int ConflictPendingCount);
