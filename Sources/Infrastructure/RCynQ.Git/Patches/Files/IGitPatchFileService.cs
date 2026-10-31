using RCynQ.Core.Exceptions;
using RCynQ.Core.State;

namespace RCynQ.Git.Patches.Files;

/// <summary>
/// Patch files of the user: an ordinary <c>git apply</c> patch of the changes of a working tree, and bringing such a patch
/// in or taking it out again.
/// </summary>
public interface IGitPatchFileService
{
    /// <summary>
    /// Builds the patch of files of a working tree against a commit. Nothing is written into the repository.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="commit">The full name of the commit the patch is built against.</param>
    /// <param name="paths">The files, relative to the working tree; those that do not differ are left out.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bytes of the patch; empty when nothing differs.</returns>
    /// <exception cref="ArgumentException"><paramref name="commit"/> is not a full commit name.</exception>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<byte[]> BuildAsync(string directory, string commit, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files a patch file changes.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="patchFile">The full path of the patch file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files, relative to the working tree, in the order of the patch; none when the file holds no patch.</returns>
    Task<IReadOnlyList<ChangedFile>> ListAsync(string directory, string patchFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a patch file to the working tree, or takes it out of it. The index, the refs and the commits stay as they
    /// are, and a patch that does not fit changes nothing.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="request">What to apply.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended.</returns>
    Task<GitPatchApplyResult> ApplyAsync(string directory, GitPatchApplyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes files out of the index, so what was staged of them does not reach the next commit.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="paths">The files, relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the index was updated.</returns>
    Task<bool> UnstageAsync(string directory, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default);
}
