namespace RCynQ.Core.State;

/// <summary>
/// Tracks the synchronization state of a node.
/// </summary>
public sealed record SyncState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncState"/> record.
    /// </summary>
    /// <param name="localStateHash">
    /// The deterministic hash of the current local state, or <see langword="null"/>
    /// when it has not been computed yet.
    /// </param>
    /// <param name="syncBaseHash">
    /// The deterministic hash of the last common base state, or <see langword="null"/>
    /// when no base has been agreed yet.
    /// </param>
    /// <param name="lastSentAtUtc">
    /// The moment the last patch was sent, in UTC, or <see langword="null"/> when
    /// nothing has been sent yet.
    /// </param>
    /// <param name="lastReceivedAtUtc">
    /// The moment the last patch was received, in UTC, or <see langword="null"/> when
    /// nothing has been received yet.
    /// </param>
    public SyncState(
        string? localStateHash,
        string? syncBaseHash,
        DateTimeOffset? lastSentAtUtc,
        DateTimeOffset? lastReceivedAtUtc)
    {
        LocalStateHash = localStateHash;
        SyncBaseHash = syncBaseHash;
        LastSentAtUtc = lastSentAtUtc;
        LastReceivedAtUtc = lastReceivedAtUtc;
    }

    /// <summary>
    /// Gets the deterministic hash of the current local state, or <see langword="null"/>
    /// when it has not been computed yet.
    /// </summary>
    public string? LocalStateHash { get; }

    /// <summary>
    /// Gets the deterministic hash of the last common base state, or <see langword="null"/>
    /// when no base has been agreed yet.
    /// </summary>
    public string? SyncBaseHash { get; }

    /// <summary>
    /// Gets the moment the last patch was sent, in UTC, or <see langword="null"/>.
    /// </summary>
    public DateTimeOffset? LastSentAtUtc { get; }

    /// <summary>
    /// Gets the moment the last patch was received, in UTC, or <see langword="null"/>.
    /// </summary>
    public DateTimeOffset? LastReceivedAtUtc { get; }
}
