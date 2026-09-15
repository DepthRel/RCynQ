using RCynQ.Core.Ids;

namespace RCynQ.Core.State;

/// <summary>
/// Describes a recorded snapshot of a node state.
/// </summary>
public sealed record SnapshotInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotInfo"/> record.
    /// </summary>
    /// <param name="id">The unique snapshot identifier.</param>
    /// <param name="node">The node the snapshot belongs to. Must not be <see langword="null"/>.</param>
    /// <param name="createdAtUtc">The moment the snapshot was taken, in UTC.</param>
    /// <param name="stateHash">
    /// The deterministic state hash captured in the snapshot. Must not be blank.
    /// </param>
    /// <param name="label">
    /// An optional human-readable label, or <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">A required reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stateHash"/> is blank.</exception>
    public SnapshotInfo(Guid id, NodeId node, DateTimeOffset createdAtUtc, string stateHash, string? label)
    {
        Id = id;
        Node = node;
        CreatedAtUtc = createdAtUtc;
        StateHash = ValidateText(stateHash);
        Label = label;
    }

    /// <summary>
    /// Gets the unique snapshot identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the node the snapshot belongs to.
    /// </summary>
    public NodeId Node { get; }

    /// <summary>
    /// Gets the moment the snapshot was taken, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the deterministic state hash captured in the snapshot.
    /// </summary>
    public string StateHash { get; }

    /// <summary>
    /// Gets an optional human-readable label, or <see langword="null"/>.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Validates that the given text is not <see langword="null"/> or blank.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>The trimmed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    private static string ValidateText(string? value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("State hash must not be blank.", nameof(value));
        }

        return trimmed;
    }
}
