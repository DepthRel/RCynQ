using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Content;

/// <summary>
/// <see cref="IPatchContent"/> over the content that a peer uploaded and the receiver verified and staged for a patch.
/// </summary>
public sealed class StoredPatchContent : IPatchContent
{
    /// <summary>
    /// The patch identifier.
    /// </summary>
    private readonly Guid _patchId;

    /// <summary>
    /// The payload storage.
    /// </summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredPatchContent"/> class.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="storage">The payload storage.</param>
    public StoredPatchContent(Guid patchId, IPatchFileStorage storage)
    {
        _patchId = patchId;
        _storage = storage;
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind == ChangeKind.Deleted || change.Sha256 is null)
        {
            throw new ArgumentException("A deleted entry has no content.", nameof(change));
        }

        return _storage.OpenContentAsync(_patchId, change.Sha256, cancellationToken);
    }
}
