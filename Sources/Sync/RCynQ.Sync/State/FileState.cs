using System.Text;
using RCynQ.Git.Hashing;

namespace RCynQ.Sync.State;

/// <summary>
/// Hashes individual working tree entries.
/// </summary>
public static class FileState
{
    /// <summary>
    /// The pseudo hash of a real directory found where a file is expected.
    /// </summary>
    public const string DirectoryMarker = "<directory>";

    /// <summary>
    /// Hashes a path: file content, symbolic link target, <see cref="DirectoryMarker"/> or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash.</returns>
    public static async Task<string?> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        var link = file.LinkTarget ?? new DirectoryInfo(path).LinkTarget;
        if (link is not null)
        {
            return DefaultStateHasher.Sha256Hex(Encoding.UTF8.GetBytes(link));
        }

        if (file.Exists)
        {
            return await DefaultStateHasher.Sha256FileAsync(path, cancellationToken);
        }

        return Directory.Exists(path) ? DirectoryMarker : null;
    }
}
