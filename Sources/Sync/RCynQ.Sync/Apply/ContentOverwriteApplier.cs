using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Paths;
using RCynQ.Localization;

namespace RCynQ.Sync.Apply;

/// <summary>
/// <see cref="IPatchApplier"/> that writes the complete incoming content of every file over the target directory, all of
/// it or nothing.
/// </summary>
/// <remarks>
/// <para>
/// A patch is applied in two steps. First everything is prepared without touching a file of the tree: every path is
/// checked and the content of every file is streamed into a staging directory inside the target (so a large file is
/// never held in memory, and moving it into place is a rename on the same volume). Only when the whole patch is ready
/// are the files put in place, one rename each; what a file replaces is moved into the staging directory instead of
/// being deleted. When one of these renames fails, the ones already made are undone in reverse order, so the tree is
/// what it was before the patch.
/// </para>
/// <para>
/// Writing below a symbolic link or creating a symbolic link that points outside the target directory is refused. Unix
/// file modes of replaced files are kept. The staging directory is removed at the end; one left behind by a process
/// that died is ignored by every scan (see <see cref="PathUtility.IsApplyStagingName"/>).
/// </para>
/// </remarks>
public sealed class ContentOverwriteApplier : IPatchApplier
{
    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOverwriteApplier"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="paths">The path utility.</param>
    public ContentOverwriteApplier(ILocalizer localizer, IPathUtility paths)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ApplyAsync(PatchEnvelope envelope, IPatchContent content, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        var root = Path.GetFullPath(Guard.NotBlank(targetDirectory, nameof(targetDirectory)));
        Directory.CreateDirectory(root);
        if (envelope.Manifest.Count == 0)
        {
            return PatchApplyResult.Applied(envelope.Id, 0);
        }

        var staging = Path.Combine(root, PathUtility.ApplyStagingPrefix + Guid.NewGuid().ToString("N"));
        var transaction = new Transaction(_localizer, root);
        try
        {
            Directory.CreateDirectory(staging);
            var steps = new List<Step>(envelope.Manifest.Count);
            for (var index = 0; index < envelope.Manifest.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                steps.Add(await PrepareAsync(root, staging, index, envelope.Manifest[index], content, cancellationToken));
            }

            // From here the tree changes. The renames are not interrupted: a patch is in place completely or not at all.
            try
            {
                foreach (var step in steps)
                {
                    try
                    {
                        transaction.Commit(step);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw new PatchApplyException(_localizer.Format("Apply.FileFailed", step.Change.Path, _localizer.Describe(ex)), ex);
                    }
                }
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            foreach (var step in steps.Where(step => step.Change.Kind == ChangeKind.Deleted))
            {
                RemoveEmptyParents(root, step.FullPath);
            }

            return PatchApplyResult.Applied(envelope.Id, steps.Count);
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    /// <summary>
    /// Checks one change and prepares everything it needs, without touching the file it is about.
    /// </summary>
    /// <param name="root">The target directory.</param>
    /// <param name="staging">The staging directory.</param>
    /// <param name="index">The position of the change in the manifest, which names its files in the staging directory.</param>
    /// <param name="change">The change.</param>
    /// <param name="content">The content of the patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The prepared step.</returns>
    /// <exception cref="PatchApplyException">The change cannot be applied.</exception>
    private async Task<Step> PrepareAsync(string root, string staging, int index, FileChange change, IPatchContent content, CancellationToken cancellationToken)
    {
        var full = _paths.ResolveWithinRoot(root, change.Path);
        if (PathUtility.HasLinkedParent(root, full))
        {
            throw new PatchApplyException($"'{change.Path}' is below a symbolic link; refusing to write through it.");
        }

        var backup = Path.Combine(staging, FormattableString.Invariant($"{index}.old"));
        try
        {
            if (change.Kind == ChangeKind.Deleted)
            {
                return new Step(change, full, backup, null, null);
            }

            if (change.IsSymlink)
            {
                await using var link = await content.OpenReadAsync(change, cancellationToken);
                using var reader = new StreamReader(link, System.Text.Encoding.UTF8);
                var target = await reader.ReadToEndAsync(cancellationToken);
                EnsureLinkStaysInside(root, full, target);
                return new Step(change, full, backup, null, target);
            }

            var prepared = Path.Combine(staging, FormattableString.Invariant($"{index}.new"));
            await using (var source = await content.OpenReadAsync(change, cancellationToken))
            await using (var destination = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var existing = new FileInfo(full);
            if (!OperatingSystem.IsWindows() && existing.Exists && existing.LinkTarget is null)
            {
                File.SetUnixFileMode(prepared, File.GetUnixFileMode(full));
            }

            return new Step(change, full, backup, prepared, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PatchApplyException(_localizer.Format("Apply.FileFailed", change.Path, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Refuses a symbolic link whose target is absolute or leaves the target directory.
    /// </summary>
    /// <param name="root">The target directory.</param>
    /// <param name="full">The full path of the link.</param>
    /// <param name="target">The link target as stored in the patch.</param>
    /// <exception cref="PatchApplyException">The link would point outside the target directory.</exception>
    private void EnsureLinkStaysInside(string root, string full, string target)
    {
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, target));
        if (Path.IsPathRooted(target) || !_paths.IsWithinRoot(root, resolved))
        {
            throw new PatchApplyException(_localizer.Format("Apply.SymlinkOutside", full, target));
        }
    }

    /// <summary>
    /// Removes directories left empty by a deletion, up to but excluding the root.
    /// </summary>
    /// <param name="root">The target root.</param>
    /// <param name="full">The deleted file path.</param>
    private static void RemoveEmptyParents(string root, string full)
    {
        try
        {
            var current = Path.GetDirectoryName(full);
            var rootTrimmed = Path.TrimEndingDirectorySeparator(root);
            while (!string.IsNullOrEmpty(current)
                && current.Length > rootTrimmed.Length
                && Directory.Exists(current)
                && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The patch is in place; an empty directory that stays behind is not a reason to report it as failed.
        }
    }

    /// <summary>
    /// Deletes the staging directory, ignoring failures: it holds only what the patch replaced or did not get to use.
    /// </summary>
    /// <param name="staging">The staging directory.</param>
    private static void DeleteQuietly(string staging)
    {
        try
        {
            var directory = new DirectoryInfo(staging);
            if (!directory.Exists)
            {
                return;
            }

            // A replaced link to a directory is removed as a link first, so the recursive delete below never sees it.
            foreach (var entry in directory.EnumerateDirectories())
            {
                if (entry.LinkTarget is not null)
                {
                    entry.Delete();
                }
            }

            directory.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover staging directory is ignored by every scan and removed by the cleanup of the node.
        }
    }

    /// <summary>
    /// One prepared change of a patch.
    /// </summary>
    /// <param name="Change">The change.</param>
    /// <param name="FullPath">The full path of the file the change is about.</param>
    /// <param name="BackupPath">Where the entry the change replaces is kept until the patch is in place.</param>
    /// <param name="PreparedFile">The prepared content of a regular file, or <see langword="null"/>.</param>
    /// <param name="LinkTarget">The target of a symbolic link to create, or <see langword="null"/>.</param>
    private sealed record Step(FileChange Change, string FullPath, string BackupPath, string? PreparedFile, string? LinkTarget);

    /// <summary>
    /// Puts prepared changes in place and remembers how to take them back.
    /// </summary>
    private sealed class Transaction
    {
        /// <summary>The target directory.</summary>
        private readonly string _root;

        /// <summary>The actions that undo what was done, newest last.</summary>
        private readonly List<Action> _undo = new();

        /// <summary>The localizer of the messages.</summary>
        private readonly ILocalizer _localizer;

        /// <summary>
        /// Initializes a new instance of the <see cref="Transaction"/> class.
        /// </summary>
        /// <param name="localizer">The localizer of the messages.</param>
        /// <param name="root">The target directory.</param>
        public Transaction(ILocalizer localizer, string root)
        {
            _localizer = localizer;
            _root = Path.TrimEndingDirectorySeparator(root);
        }

        /// <summary>
        /// Puts one change in place.
        /// </summary>
        /// <param name="step">The prepared change.</param>
        /// <exception cref="IOException">The change could not be put in place.</exception>
        public void Commit(Step step)
        {
            if (step.Change.Kind != ChangeKind.Deleted)
            {
                CreateParents(Path.GetDirectoryName(step.FullPath)!);
            }

            SetAside(step);
            if (step.PreparedFile is not null)
            {
                File.Move(step.PreparedFile, step.FullPath);
                _undo.Add(() => File.Delete(step.FullPath));
            }
            else if (step.LinkTarget is not null)
            {
                File.CreateSymbolicLink(step.FullPath, step.LinkTarget);
                _undo.Add(() => File.Delete(step.FullPath));
            }
        }

        /// <summary>
        /// Takes back everything that was put in place, newest first. A step that cannot be taken back is skipped: the
        /// others are still worth restoring, and the failure that started the rollback is the one that is reported.
        /// </summary>
        public void Rollback()
        {
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    _undo[i]();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // See the summary.
                }
            }

            _undo.Clear();
        }

        /// <summary>
        /// Moves the entry a change replaces into the staging directory. A real directory is never replaced.
        /// </summary>
        /// <param name="step">The prepared change.</param>
        /// <exception cref="IOException">A directory is in the place of the file.</exception>
        private void SetAside(Step step)
        {
            // The directory is asked first: a link to a directory is a directory for the file system.
            var directory = new DirectoryInfo(step.FullPath);
            if (directory.Exists)
            {
                if (directory.LinkTarget is null)
                {
                    throw new IOException(_localizer.Get(step.Change.Kind == ChangeKind.Deleted ? "Apply.DirectoryInPlaceOfFile" : "Apply.DirectoryInPlaceOfFileTarget"));
                }

                Directory.Move(step.FullPath, step.BackupPath);
                _undo.Add(() => Directory.Move(step.BackupPath, step.FullPath));
                return;
            }

            var file = new FileInfo(step.FullPath);
            if (file.Exists || file.LinkTarget is not null)
            {
                File.Move(step.FullPath, step.BackupPath);
                _undo.Add(() => File.Move(step.BackupPath, step.FullPath));
            }
        }

        /// <summary>
        /// Creates the missing directories above a file and remembers to remove them again.
        /// </summary>
        /// <param name="directory">The directory the file goes into.</param>
        private void CreateParents(string directory)
        {
            var missing = new Stack<string>();
            var current = directory;
            while (!string.IsNullOrEmpty(current) && current.Length > _root.Length && !Directory.Exists(current))
            {
                missing.Push(current);
                current = Path.GetDirectoryName(current);
            }

            foreach (var created in missing)
            {
                Directory.CreateDirectory(created);
                _undo.Add(() =>
                {
                    if (Directory.Exists(created) && !Directory.EnumerateFileSystemEntries(created).Any())
                    {
                        Directory.Delete(created);
                    }
                });
            }
        }
    }
}
