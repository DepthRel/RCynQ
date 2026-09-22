using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Scans working trees and builds file change manifests.
/// </summary>
/// <remarks>
/// Besides the configured excluded directory names, a scan honours the <c>.gitignore</c> files of the tree: files and
/// directories they describe are not part of any snapshot, base or patch.
/// </remarks>
public interface IFileManifestBuilder
{
    /// <summary>
    /// Scans a directory and describes every included file, applying the <c>.gitignore</c> files of that directory.
    /// </summary>
    /// <param name="directory">The directory to scan. A missing directory is treated as empty.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file entries ordered by path (ordinal).</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a directory and applies the <c>.gitignore</c> files of another directory.
    /// </summary>
    /// <param name="directory">The directory to scan. A missing directory is treated as empty.</param>
    /// <param name="ignoreRulesDirectory">
    /// The directory whose <c>.gitignore</c> files decide what is skipped. Comparing a sync base with a working tree
    /// scans both with the rules of the working tree, so a rule added to a <c>.gitignore</c> hides the files from both
    /// sides instead of turning them into deletions.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file entries ordered by path (ordinal).</returns>
    /// <exception cref="ArgumentException">A directory path is blank.</exception>
    Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, string ignoreRulesDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the base directory with the incoming directory, applying the <c>.gitignore</c> files of the incoming directory to both.
    /// </summary>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="incomingDirectory">The incoming directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file changes ordered by path; empty when both states are equal.</returns>
    /// <exception cref="ArgumentException">A directory path is blank.</exception>
    Task<IReadOnlyList<FileChange>> BuildAsync(string baseDirectory, string incomingDirectory, CancellationToken cancellationToken = default);
}
