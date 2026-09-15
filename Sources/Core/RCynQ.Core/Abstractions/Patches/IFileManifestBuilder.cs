using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Scans working trees and builds file change manifests.
/// </summary>
public interface IFileManifestBuilder
{
    /// <summary>
    /// Scans a directory and describes every included file.
    /// </summary>
    /// <param name="directory">The directory to scan. A missing directory is treated as empty.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file entries ordered by path (ordinal).</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the base directory with the incoming directory.
    /// </summary>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="incomingDirectory">The incoming directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file changes ordered by path; empty when both states are equal.</returns>
    /// <exception cref="ArgumentException">A directory path is blank.</exception>
    Task<IReadOnlyList<FileChange>> BuildAsync(string baseDirectory, string incomingDirectory, CancellationToken cancellationToken = default);
}
