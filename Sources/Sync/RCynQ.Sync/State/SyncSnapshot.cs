using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Sync.State;

/// <summary>
/// The result of <see cref="StateTracker.CaptureAsync"/>.
/// </summary>
/// <param name="State">The stored state.</param>
/// <param name="SyncBase">The status of the sync base.</param>
/// <param name="PendingChanges">The changes of the working tree the peer does not have yet; empty without a sync base.</param>
/// <param name="ChangedFiles">Every file that differs from the Git commit, including those the peer already has; empty without a sync base.</param>
public sealed record SyncSnapshot(SyncState State, SyncBaseStatus SyncBase, IReadOnlyList<FileChange> PendingChanges, IReadOnlyList<ChangedFile> ChangedFiles);
