using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Diff;

namespace RCynQ.Sync.History;

/// <summary>
/// Builds and stores the diff of a patch that is shown to the user.
/// </summary>
/// <remarks>
/// The diff compares the content of a patch with the directory the patch was built against. It has to be taken before
/// the patch changes that directory, so both the sender and the receiver store it at the start of their work.
/// </remarks>
public sealed class PatchDisplayDiffStore
{
    /// <summary>The diff builder.</summary>
    private readonly IPatchDiffBuilder _diffs;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchDisplayDiffStore"/> class.
    /// </summary>
    /// <param name="diffs">The diff builder.</param>
    /// <param name="storage">The payload storage.</param>
    public PatchDisplayDiffStore(IPatchDiffBuilder diffs, IPatchFileStorage storage)
    {
        _diffs = diffs ?? throw new ArgumentNullException(nameof(diffs));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Builds the diff of a patch against a directory and stores it.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <param name="content">The source of the content the patch brings.</param>
    /// <param name="baseDirectory">The directory the patch is compared with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the diff is stored, or skipped because Git is not available.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public async Task SaveAsync(PatchEnvelope envelope, IPatchContent content, string baseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        try
        {
            var diff = await _diffs.BuildAsync(envelope.Manifest, baseDirectory, content, PatchDiffLimits.Display, cancellationToken);
            await _storage.SaveDiffAsync(envelope.Id, diff, cancellationToken);
        }
        catch (GitExecutionException)
        {
            // The diff is a convenience for the user; a missing Git must not stop the patch.
        }
    }
}
