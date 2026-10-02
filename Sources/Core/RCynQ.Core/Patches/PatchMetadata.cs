using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// Patch metadata stored in the local database.
/// </summary>
/// <remarks>
/// Binary payloads (diff and bundle) are stored as files by <c>IPatchFileStorage</c>.
/// </remarks>
public sealed record PatchMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchMetadata"/> record.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="direction">Whether the patch was sent or received.</param>
    /// <param name="sourceNode">The sender node.</param>
    /// <param name="targetNode">The receiver node.</param>
    /// <param name="createdAtUtc">The creation time.</param>
    /// <param name="baseStateHash">The sender's sync base hash.</param>
    /// <param name="incomingStateHash">The sender's working tree hash.</param>
    /// <param name="status">The patch status.</param>
    /// <param name="appliedAtUtc">The time the patch reached a final state, or <see langword="null"/>.</param>
    /// <param name="manifest">The file changes.</param>
    /// <param name="conflicts">The current conflicts; <see langword="null"/> is treated as empty.</param>
    /// <param name="error">The last error, or <see langword="null"/>.</param>
    /// <param name="signature">The envelope signature bytes, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A value is blank or an enum is not defined.</exception>
    public PatchMetadata(
        Guid id,
        PatchDirection direction,
        NodeId sourceNode,
        NodeId targetNode,
        DateTimeOffset createdAtUtc,
        string baseStateHash,
        string incomingStateHash,
        PatchStatus status,
        DateTimeOffset? appliedAtUtc,
        IReadOnlyList<FileChange> manifest,
        IReadOnlyList<ConflictFile>? conflicts = null,
        string? error = null,
        byte[]? signature = null)
    {
        Id = id;
        Direction = Guard.Defined(direction, nameof(direction));
        SourceNode = sourceNode;
        TargetNode = targetNode;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        BaseStateHash = Guard.NotBlank(baseStateHash, nameof(baseStateHash));
        IncomingStateHash = Guard.NotBlank(incomingStateHash, nameof(incomingStateHash));
        Status = Guard.Defined(status, nameof(status));
        AppliedAtUtc = appliedAtUtc?.ToUniversalTime();
        Manifest = Guard.NotNull(manifest, nameof(manifest));
        Conflicts = conflicts ?? Array.Empty<ConflictFile>();
        Error = error;
        Signature = signature ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Gets the patch identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets whether the patch was sent or received.
    /// </summary>
    public PatchDirection Direction { get; }

    /// <summary>
    /// Gets the sender node.
    /// </summary>
    public NodeId SourceNode { get; }

    /// <summary>
    /// Gets the receiver node.
    /// </summary>
    public NodeId TargetNode { get; }

    /// <summary>
    /// Gets the creation time in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the sender's sync base hash.
    /// </summary>
    public string BaseStateHash { get; }

    /// <summary>
    /// Gets the sender's working tree hash.
    /// </summary>
    public string IncomingStateHash { get; }

    /// <summary>
    /// Gets the patch status.
    /// </summary>
    public PatchStatus Status { get; }

    /// <summary>
    /// Gets the time the patch reached a final state, or <see langword="null"/>.
    /// </summary>
    public DateTimeOffset? AppliedAtUtc { get; }

    /// <summary>
    /// Gets the file changes.
    /// </summary>
    public IReadOnlyList<FileChange> Manifest { get; }

    /// <summary>
    /// Gets the current conflicts.
    /// </summary>
    public IReadOnlyList<ConflictFile> Conflicts { get; }

    /// <summary>
    /// Gets the last error, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets the envelope signature bytes.
    /// </summary>
    public byte[] Signature { get; }

    /// <summary>
    /// Gets the name of the repository the patch belongs to, or <see langword="null"/> for a patch exchanged before the
    /// node knew the name of its repository (it belongs to the repository the node started with).
    /// </summary>
    public string? Repository { get; init; }

    /// <summary>
    /// Creates metadata describing the given envelope.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="direction">The patch direction.</param>
    /// <param name="status">The initial status.</param>
    /// <returns>The metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    public static PatchMetadata FromEnvelope(PatchEnvelope envelope, PatchDirection direction, PatchStatus status = PatchStatus.Pending)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new PatchMetadata(
            envelope.Id,
            direction,
            envelope.SourceNode,
            envelope.TargetNode,
            envelope.CreatedAtUtc,
            envelope.BaseStateHash,
            envelope.IncomingStateHash,
            status,
            null,
            envelope.Manifest,
            null,
            null,
            envelope.Signature)
        {
            Repository = envelope.Repository
        };
    }

    /// <summary>
    /// Creates a copy with the outcome of a processing step.
    /// </summary>
    /// <param name="result">The processing result.</param>
    /// <param name="timestampUtc">The time of the outcome.</param>
    /// <returns>The updated metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public PatchMetadata WithResult(PatchApplyResult result, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        var finalTime = result.Status is PatchStatus.Applied or PatchStatus.ConflictResolved or PatchStatus.Rejected
            ? timestampUtc
            : AppliedAtUtc;
        return new PatchMetadata(
            Id, Direction, SourceNode, TargetNode, CreatedAtUtc, BaseStateHash, IncomingStateHash,
            result.Status, finalTime, Manifest, result.Conflicts, result.Error, Signature)
        {
            Repository = Repository
        };
    }
}
