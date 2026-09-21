using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Patches.Bundles;

/// <summary>
/// Builds and reads zip file bundles carrying the complete incoming content of a patch.
/// </summary>
public interface IFileBundleBuilder
{
    /// <summary>
    /// Builds a bundle for the given changes.
    /// </summary>
    /// <param name="changes">The manifest.</param>
    /// <param name="incomingDirectory">The directory with the incoming content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The zip bytes.</returns>
    /// <exception cref="SyncBridgeException">A file changed after the manifest was built.</exception>
    Task<byte[]> BuildAsync(IReadOnlyList<FileChange> changes, string incomingDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bundle and, when a manifest is given, verifies that it matches exactly.
    /// </summary>
    /// <param name="bundle">The zip bytes.</param>
    /// <param name="expectedManifest">The manifest to verify against, or <see langword="null"/>.</param>
    /// <returns>The decoded bundle.</returns>
    /// <exception cref="FormatException">The bundle is malformed, unsafe or does not match the manifest.</exception>
    FileBundle Read(byte[] bundle, IReadOnlyList<FileChange>? expectedManifest = null);
}
