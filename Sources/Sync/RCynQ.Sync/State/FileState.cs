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
    public static Task<string?> HashAsync(string path, CancellationToken cancellationToken = default)
        => HashAsync(path, normalizeLineEndings: false, cancellationToken);

    /// <summary>
    /// Hashes a path like <see cref="HashAsync(string, CancellationToken)"/>, but counts every CRLF of a text file as LF, so the
    /// same content compares equal however Git checked it out (see <see cref="DefaultStateHasher.Sha256TextNormalizedFileAsync"/>).
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash.</returns>
    public static Task<string?> ContentHashAsync(string path, CancellationToken cancellationToken = default)
        => HashAsync(path, normalizeLineEndings: true, cancellationToken);

    /// <summary>
    /// Hashes a path.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="normalizeLineEndings">Whether a CRLF of a text file counts as LF.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash.</returns>
    private static async Task<string?> HashAsync(string path, bool normalizeLineEndings, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        var link = file.LinkTarget ?? new DirectoryInfo(path).LinkTarget;
        if (link is not null)
        {
            return DefaultStateHasher.Sha256Hex(Encoding.UTF8.GetBytes(link));
        }

        if (file.Exists)
        {
            return normalizeLineEndings
                ? await DefaultStateHasher.Sha256TextNormalizedFileAsync(path, cancellationToken)
                : await DefaultStateHasher.Sha256FileAsync(path, cancellationToken);
        }

        return Directory.Exists(path) ? DirectoryMarker : null;
    }
}
