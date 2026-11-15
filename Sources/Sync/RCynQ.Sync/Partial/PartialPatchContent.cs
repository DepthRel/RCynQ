using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Partial;

/// <summary>
/// <see cref="IPatchContent"/> that gives the chosen version of a file with a choice of a part of its changes, and the
/// working file of every other file.
/// </summary>
public sealed class PartialPatchContent : IPatchContent
{
    /// <summary>The content of the files without a choice.</summary>
    private readonly IPatchContent _inner;

    /// <summary>The choices by path.</summary>
    private readonly IReadOnlyDictionary<string, PartialChange> _partials;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialPatchContent"/> class.
    /// </summary>
    /// <param name="inner">The content of the files without a choice.</param>
    /// <param name="partials">The choices by path.</param>
    public PartialPatchContent(IPatchContent inner, IReadOnlyDictionary<string, PartialChange> partials)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _partials = partials ?? throw new ArgumentNullException(nameof(partials));
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        return _partials.TryGetValue(change.Path, out var partial) && change.Kind != ChangeKind.Deleted
            ? Task.FromResult<Stream>(new MemoryStream(partial.Content, writable: false))
            : _inner.OpenReadAsync(change, cancellationToken);
    }

    /// <summary>
    /// Puts the chosen versions into the changes of a patch: their hash and size instead of those of the working file.
    /// </summary>
    /// <param name="changes">The changes of the patch.</param>
    /// <param name="partials">The choices by path.</param>
    /// <returns>The changes, in the same order.</returns>
    public static IReadOnlyList<FileChange> Substitute(IReadOnlyList<FileChange> changes, IReadOnlyDictionary<string, PartialChange> partials)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(partials);
        if (partials.Count == 0)
        {
            return changes;
        }

        return changes
            .Select(change => change.Kind != ChangeKind.Deleted && !change.IsSymlink && partials.TryGetValue(change.Path, out var partial)
                ? new FileChange(change.Kind, change.Path, DefaultStateHasher.Sha256Hex(partial.Content), partial.Content.Length, false, change.BaseSha256)
                : change)
            .ToArray();
    }

    /// <summary>
    /// Keeps only the choices of the given changes.
    /// </summary>
    /// <param name="changes">The changes of a patch.</param>
    /// <param name="partials">The choices by path.</param>
    /// <returns>The choices of the files of the patch.</returns>
    public static IReadOnlyDictionary<string, PartialChange> Of(IReadOnlyList<FileChange> changes, IReadOnlyDictionary<string, PartialChange> partials)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(partials);
        return changes
            .Where(change => change.Kind != ChangeKind.Deleted && !change.IsSymlink && partials.ContainsKey(change.Path))
            .ToDictionary(change => change.Path, change => partials[change.Path], SyncBaseManager.PathComparer);
    }
}
