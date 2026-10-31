using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Apply;

/// <summary>
/// The content a merge writes: the merged files from a temporary directory, everything else from the patch.
/// </summary>
public sealed class MergedPatchContent : IPatchContent
{
    /// <summary>The content of the patch.</summary>
    private readonly IPatchContent _incoming;

    /// <summary>The merged files by the path of their change.</summary>
    private readonly IReadOnlyDictionary<string, string> _merged;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergedPatchContent"/> class.
    /// </summary>
    /// <param name="incoming">The content of the patch.</param>
    /// <param name="merged">The merged files by the path of their change.</param>
    public MergedPatchContent(IPatchContent incoming, IReadOnlyDictionary<string, string> merged)
    {
        _incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
        _merged = merged ?? throw new ArgumentNullException(nameof(merged));
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        return _merged.TryGetValue(change.Path, out var file)
            ? Task.FromResult<Stream>(File.OpenRead(file))
            : _incoming.OpenReadAsync(change, cancellationToken);
    }
}
