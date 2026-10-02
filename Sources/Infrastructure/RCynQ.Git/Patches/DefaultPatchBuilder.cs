using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Manifest;

namespace RCynQ.Git.Patches;

/// <summary>
/// Builds patch envelopes: the manifest between two directories and the state hashes of both sides.
/// </summary>
/// <remarks>
/// The envelope holds no file content. The content is read from the incoming directory while the patch is sent, one file
/// at a time, and every file is verified against the hash in the manifest, so a file edited during the transfer is detected
/// instead of being sent in a state the manifest does not describe.
/// </remarks>
public sealed class DefaultPatchBuilder : IPatchBuilder
{
    /// <summary>
    /// The manifest builder.
    /// </summary>
    private readonly IFileManifestBuilder _manifestBuilder;

    /// <summary>
    /// The state hasher.
    /// </summary>
    private readonly IStateHasher _hasher;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPatchBuilder"/> class.
    /// </summary>
    /// <param name="manifestBuilder">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="timeProvider">The clock.</param>
    public DefaultPatchBuilder(IFileManifestBuilder manifestBuilder, IStateHasher hasher, TimeProvider timeProvider)
    {
        _manifestBuilder = manifestBuilder;
        _hasher = hasher;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PatchEnvelope> BuildAsync(
        NodeId sourceNode,
        NodeId targetNode,
        string baseDirectory,
        string incomingDirectory,
        CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(baseDirectory, nameof(baseDirectory));
        Guard.NotBlank(incomingDirectory, nameof(incomingDirectory));

        // Both sides are scanned with the .gitignore rules of the incoming tree, see IFileManifestBuilder.BuildAsync.
        var baseEntries = await _manifestBuilder.ScanAsync(baseDirectory, incomingDirectory, cancellationToken);
        var incomingEntries = await _manifestBuilder.ScanAsync(incomingDirectory, incomingDirectory, cancellationToken);
        var manifest = DefaultFileManifestBuilder.Compare(baseEntries, incomingEntries);

        return new PatchEnvelope(
            Guid.NewGuid(),
            sourceNode,
            targetNode,
            _timeProvider.GetUtcNow(),
            _hasher.ComputeHash(baseEntries),
            _hasher.ComputeHash(incomingEntries),
            manifest);
    }
}
