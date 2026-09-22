using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// A live patch exchanged between Source and Dest.
/// </summary>
/// <remarks>
/// The envelope carries the file manifest only. The content of added and modified files travels separately, one file
/// per request, and is verified against the SHA-256 hashes of the manifest; the unified diff is never transferred, the
/// receiver builds it from its own sync base when it is needed.
/// The <see cref="Signature"/> covers the canonical payload of every other field, so the signed manifest also
/// authenticates every file that is sent afterwards.
/// </remarks>
public sealed record PatchEnvelope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchEnvelope"/> record.
    /// </summary>
    /// <param name="id">The unique patch identifier.</param>
    /// <param name="sourceNode">The sender node.</param>
    /// <param name="targetNode">The receiver node.</param>
    /// <param name="createdAtUtc">The creation time.</param>
    /// <param name="baseStateHash">The state hash of the sender's sync base.</param>
    /// <param name="incomingStateHash">The state hash of the sender's working tree at build time.</param>
    /// <param name="manifest">The ordered file changes.</param>
    /// <param name="signature">The signature bytes, or <see langword="null"/> for an unsigned envelope.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A node identifier is empty, the identifier is empty or a hash is blank.</exception>
    public PatchEnvelope(
        Guid id,
        NodeId sourceNode,
        NodeId targetNode,
        DateTimeOffset createdAtUtc,
        string baseStateHash,
        string incomingStateHash,
        IReadOnlyList<FileChange> manifest,
        byte[]? signature = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Patch identifier must not be empty.", nameof(id));
        }

        if (sourceNode.IsEmpty)
        {
            throw new ArgumentException("Source node must not be empty.", nameof(sourceNode));
        }

        if (targetNode.IsEmpty)
        {
            throw new ArgumentException("Target node must not be empty.", nameof(targetNode));
        }

        Id = id;
        SourceNode = sourceNode;
        TargetNode = targetNode;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        BaseStateHash = Guard.NotBlank(baseStateHash, nameof(baseStateHash));
        IncomingStateHash = Guard.NotBlank(incomingStateHash, nameof(incomingStateHash));
        Manifest = Guard.NotNull(manifest, nameof(manifest));
        Signature = signature ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Gets the unique patch identifier.
    /// </summary>
    public Guid Id { get; }

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
    /// Gets the state hash of the sender's sync base.
    /// </summary>
    public string BaseStateHash { get; }

    /// <summary>
    /// Gets the state hash of the sender's working tree at build time.
    /// </summary>
    public string IncomingStateHash { get; }

    /// <summary>
    /// Gets the ordered file changes.
    /// </summary>
    public IReadOnlyList<FileChange> Manifest { get; }

    /// <summary>
    /// Gets the distinct SHA-256 hashes of the file content the receiver needs, in manifest order.
    /// </summary>
    /// <remarks>
    /// Added and modified entries need content; a symbolic link needs the UTF-8 text of its target. Entries with the
    /// same content share one hash, so the content is sent once.
    /// </remarks>
    public IReadOnlyList<string> ContentHashes => Manifest
        .Where(change => change.Kind != ChangeKind.Deleted && change.Sha256 is not null)
        .Select(change => change.Sha256!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Gets the signature bytes; empty for an unsigned envelope.
    /// </summary>
    public byte[] Signature { get; }

    /// <summary>
    /// Gets a value indicating whether the envelope carries a signature.
    /// </summary>
    public bool IsSigned => Signature.Length > 0;

    /// <summary>
    /// Creates a copy of the envelope with the given signature.
    /// </summary>
    /// <param name="signature">The signature bytes.</param>
    /// <returns>The signed envelope.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signature"/> is <see langword="null"/>.</exception>
    public PatchEnvelope WithSignature(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return new PatchEnvelope(Id, SourceNode, TargetNode, CreatedAtUtc, BaseStateHash, IncomingStateHash, Manifest, signature);
    }
}
