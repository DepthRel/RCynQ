using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// A live patch exchanged between Workstation and Origin.
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
    /// <param name="repository">The name of the repository the patch belongs to, or <see langword="null"/>.</param>
    /// <param name="commits">The commits the envelope delivers instead of files, or <see langword="null"/> for a patch of files.</param>
    public PatchEnvelope(
        Guid id,
        NodeId sourceNode,
        NodeId targetNode,
        DateTimeOffset createdAtUtc,
        string baseStateHash,
        string incomingStateHash,
        IReadOnlyList<FileChange> manifest,
        byte[]? signature = null,
        string? repository = null,
        CommitDelivery? commits = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Patch identifier must not be empty.", nameof(id));
        }

        if (sourceNode.IsEmpty)
        {
            throw new ArgumentException("Sender node must not be empty.", nameof(sourceNode));
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
        if (repository is not null && !RepositoryName.IsValid(repository))
        {
            throw new ArgumentException($"Repository name \"{repository}\" is not valid.", nameof(repository));
        }

        if (commits is not null && Manifest.Count > 0)
        {
            throw new ArgumentException("A delivery of commits carries no files.", nameof(commits));
        }

        Repository = repository;
        Commits = commits;
    }

    /// <summary>
    /// Gets the commits the envelope delivers instead of files, or <see langword="null"/> for a patch of files. They are
    /// part of what is signed.
    /// </summary>
    public CommitDelivery? Commits { get; }

    /// <summary>
    /// Gets a value indicating whether the envelope delivers commits rather than files.
    /// </summary>
    public bool IsCommitDelivery => Commits is not null;

    /// <summary>
    /// Gets the name of the repository the patch belongs to, or <see langword="null"/> when the sender does not know the
    /// name of its repository yet. The name is part of what is signed.
    /// </summary>
    public string? Repository { get; }

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
    /// same content share one hash, so the content is sent once. A delivery of commits needs only its bundle.
    /// </remarks>
    public IReadOnlyList<string> ContentHashes => Commits is not null
        ? new[] { Commits.BundleSha256 }
        : Manifest
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
        return new PatchEnvelope(Id, SourceNode, TargetNode, CreatedAtUtc, BaseStateHash, IncomingStateHash, Manifest, signature, Repository, Commits);
    }
}
