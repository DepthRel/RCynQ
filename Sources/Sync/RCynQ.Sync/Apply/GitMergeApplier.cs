using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Localization;
using RCynQ.Sync.Backups;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Apply;

/// <summary>
/// Merges an incoming patch into a working tree whose files were changed locally: a three-way merge of every file,
/// with the sync base as the common ancestor.
/// </summary>
/// <remarks>
/// <para>
/// A conflict means the receiver changed a file since the last synchronization, and those changes are usually not
/// committed. <c>git apply --3way</c> refuses such a file ("does not match index"), so it cannot do this job. A
/// three-way merge of the file itself can: <c>git merge-file</c> takes the local file, the sync base and the incoming
/// file, needs no repository and no clean working tree, and leaves the usual conflict markers where both sides changed
/// the same lines.
/// </para>
/// <para>
/// The merge is worked out in a temporary directory first. When Git cannot merge one of the files at all (a binary
/// file changed on both sides, a file one side deleted and the other changed), nothing is written and the result names
/// those files, so the user decides with one of the other policies. Otherwise the local versions are backed up and the
/// outcome is written in one transaction. A file with conflict markers is a finished merge that needs an edit, not a
/// failure: it is reported in the result.
/// </para>
/// <para>
/// The line endings of the local file win. The sync base and the incoming file are brought to them before the merge;
/// otherwise a file checked out with CRLF on one node and LF on the other would collide on every line.
/// </para>
/// </remarks>
public sealed class GitMergeApplier
{
    /// <summary>The marker label of the local side.</summary>
    private const string LOCAL_LABEL = "local";

    /// <summary>The marker label of the common ancestor.</summary>
    private const string BASE_LABEL = "base";

    /// <summary>The marker label of the incoming side.</summary>
    private const string INCOMING_LABEL = "incoming";

    /// <summary>The largest exit code of <c>git merge-file</c> that counts conflicts; above it the merge itself failed.</summary>
    private const int MAX_CONFLICT_EXIT_CODE = 127;

    /// <summary>How much of a file is looked at to tell text from binary, as Git does.</summary>
    private const int BINARY_PROBE_BYTES = 8000;

    /// <summary>The Git command runner.</summary>
    private readonly IGitCommandRunner _git;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _paths;

    /// <summary>Writes the outcome of the merge in one transaction.</summary>
    private readonly ContentOverwriteApplier _applier;

    /// <summary>Backs up the local versions before they are replaced.</summary>
    private readonly BackupService _backups;

    /// <summary>The node data directories, or <see langword="null"/> to work in the system temporary directory.</summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeApplier"/> class.
    /// </summary>
    /// <param name="git">The Git command runner.</param>
    /// <param name="paths">The path utility.</param>
    /// <param name="applier">Writes the outcome of the merge in one transaction.</param>
    /// <param name="backups">Backs up the local versions before they are replaced.</param>
    /// <param name="appData">The node data directories, or <see langword="null"/> to work in the system temporary directory.</param>
    public GitMergeApplier(IGitCommandRunner git, IPathUtility paths, ContentOverwriteApplier applier, BackupService backups, IAppDataFactory? appData = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _backups = backups ?? throw new ArgumentNullException(nameof(backups));
        _appData = appData;
    }

    /// <summary>
    /// Merges a patch into a working tree.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <param name="content">The source of the incoming content.</param>
    /// <param name="baseDirectory">The sync base: the state both sides started from.</param>
    /// <param name="targetDirectory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="PatchStatus.Applied"/> when the merge was written, with the files that have conflict markers left in
    /// them as <see cref="PatchApplyResult.Conflicts"/>; <see cref="PatchStatus.ConflictPending"/> with the files Git
    /// cannot merge when nothing was written.
    /// </returns>
    /// <exception cref="SyncBridgeException">Git could not be started.</exception>
    /// <exception cref="PatchApplyException">The outcome could not be written; the working tree is as it was.</exception>
    public async Task<PatchApplyResult> MergeAsync(PatchEnvelope envelope, IPatchContent content, string baseDirectory, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        var root = Path.GetFullPath(Guard.NotBlank(targetDirectory, nameof(targetDirectory)));
        var ancestor = Path.GetFullPath(Guard.NotBlank(baseDirectory, nameof(baseDirectory)));
        var scratch = Path.Combine(_appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq"), "merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var unmergeable = new List<ConflictFile>();
            var marked = new List<ConflictFile>();
            var manifest = new List<FileChange>();
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < envelope.Manifest.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var change = envelope.Manifest[index];
                var outcome = await PlanAsync(index, change, content, ancestor, root, scratch, cancellationToken);
                switch (outcome.Kind)
                {
                    case OutcomeKind.TakeIncoming:
                        manifest.Add(change);
                        break;
                    case OutcomeKind.Merged:
                    case OutcomeKind.MergedWithMarkers:
                        manifest.Add(outcome.Change!);
                        merged[change.Path] = outcome.File!;
                        if (outcome.Kind == OutcomeKind.MergedWithMarkers)
                        {
                            marked.Add(new ConflictFile(change.Path, null, change.Sha256, LocalizedText.Encode("Merge.Markers")));
                        }

                        break;
                    case OutcomeKind.CannotMerge:
                        unmergeable.Add(new ConflictFile(change.Path, null, change.Sha256, outcome.Reason));
                        break;
                }
            }

            if (unmergeable.Count > 0)
            {
                return PatchApplyResult.Conflict(envelope.Id, unmergeable);
            }

            if (manifest.Count > 0)
            {
                await _backups.BackupAsync(envelope.Id, root, manifest.Select(change => change.Path), cancellationToken);
                var outcome = new PatchEnvelope(
                    envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, manifest, repository: envelope.Repository);
                await _applier.ApplyAsync(outcome, new MergedContent(content, merged), root, cancellationToken);
            }

            return new PatchApplyResult(envelope.Id, PatchStatus.Applied, envelope.Manifest.Count, marked, null);
        }
        finally
        {
            DeleteQuietly(scratch);
        }
    }

    /// <summary>
    /// Works out what the merge does with one file, without touching the working tree.
    /// </summary>
    /// <param name="index">The position of the change in the patch; it names the temporary files.</param>
    /// <param name="change">The change.</param>
    /// <param name="content">The source of the incoming content.</param>
    /// <param name="ancestor">The sync base.</param>
    /// <param name="root">The working tree.</param>
    /// <param name="scratch">The temporary directory of the merge.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome for the file.</returns>
    private async Task<Outcome> PlanAsync(int index, FileChange change, IPatchContent content, string ancestor, string root, string scratch, CancellationToken cancellationToken)
    {
        var local = _paths.ResolveWithinRoot(root, change.Path);
        var common = _paths.ResolveWithinRoot(ancestor, change.Path);
        if (Directory.Exists(local) && new DirectoryInfo(local).LinkTarget is null)
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Conflict.DirectoryLocally"));
        }

        var theirs = Path.Combine(scratch, FormattableString.Invariant($"{index}.theirs"));
        string? theirsHash = null;
        if (change.Kind != ChangeKind.Deleted)
        {
            await using (var source = await content.OpenReadAsync(change, cancellationToken))
            await using (var destination = File.Create(theirs))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            // The content of a link is its target, which is compared as it is; text is compared whatever its line endings.
            theirsHash = change.IsSymlink
                ? DefaultStateHasher.Sha256Hex(await File.ReadAllBytesAsync(theirs, cancellationToken))
                : await DefaultStateHasher.Sha256TextNormalizedFileAsync(theirs, cancellationToken);
        }

        var oursHash = await FileState.ContentHashAsync(local, cancellationToken);
        if (oursHash == theirsHash)
        {
            return Outcome.Unchanged;
        }

        if (oursHash == await FileState.ContentHashAsync(common, cancellationToken))
        {
            return Outcome.TakeIncoming;
        }

        if (change.Kind == ChangeKind.Deleted)
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Merge.DeletedBySender"));
        }

        if (oursHash is null)
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Merge.DeletedLocally"));
        }

        if (change.IsSymlink || new FileInfo(local).LinkTarget is not null)
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Merge.Link"));
        }

        var ours = Path.Combine(scratch, FormattableString.Invariant($"{index}.ours"));
        var @base = Path.Combine(scratch, FormattableString.Invariant($"{index}.base"));
        File.Copy(local, ours);
        if (File.Exists(common))
        {
            File.Copy(common, @base);
        }
        else
        {
            // Both sides added the file: they have nothing in common.
            await File.WriteAllBytesAsync(@base, Array.Empty<byte>(), cancellationToken);
        }

        if (IsBinary(ours) || IsBinary(@base) || IsBinary(theirs))
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Merge.Binary"));
        }

        await AdoptLineEndingsAsync(ours, new[] { @base, theirs }, cancellationToken);
        var result = await _git.RunAsync(
            new[] { "merge-file", "-L", LOCAL_LABEL, "-L", BASE_LABEL, "-L", INCOMING_LABEL, Path.GetFileName(ours), Path.GetFileName(@base), Path.GetFileName(theirs) },
            scratch,
            cancellationToken: cancellationToken);
        if (result.ExitCode is < 0 or > MAX_CONFLICT_EXIT_CODE)
        {
            return Outcome.CannotMerge(LocalizedText.Encode("Merge.Failed", result.StandardError.Trim()));
        }

        var merged = new FileChange(ChangeKind.Modified, change.Path, await DefaultStateHasher.Sha256FileAsync(ours, cancellationToken), new FileInfo(ours).Length, false);
        return new Outcome(result.ExitCode == 0 ? OutcomeKind.Merged : OutcomeKind.MergedWithMarkers, merged, ours, null);
    }

    /// <summary>
    /// Tells whether a file is binary the way Git does: a zero byte near its start.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> for a binary file.</returns>
    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[BINARY_PROBE_BYTES];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>
    /// Brings files to the line endings of the local file, when that file uses one kind of them throughout.
    /// </summary>
    /// <param name="local">The local file, which is left as it is.</param>
    /// <param name="others">The files to bring to its line endings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the files are rewritten.</returns>
    private static async Task AdoptLineEndingsAsync(string local, IReadOnlyList<string> others, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(local, cancellationToken);
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
            {
                continue;
            }

            if (i > 0 && bytes[i - 1] == (byte)'\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        if (crlf > 0 && lf > 0)
        {
            // A file with both kinds has no style to adopt.
            return;
        }

        var useCrlf = crlf > 0;
        foreach (var other in others)
        {
            var source = await File.ReadAllBytesAsync(other, cancellationToken);
            var target = new List<byte>(source.Length + (useCrlf ? source.Length / 20 : 0));
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (value == (byte)'\r' && i + 1 < source.Length && source[i + 1] == (byte)'\n')
                {
                    // The carriage return of a CRLF pair is written, or left out, with its line feed.
                    continue;
                }

                if (value == (byte)'\n' && useCrlf)
                {
                    target.Add((byte)'\r');
                }

                target.Add(value);
            }

            await File.WriteAllBytesAsync(other, target.ToArray(), cancellationToken);
        }
    }

    /// <summary>
    /// Removes the temporary directory of a merge.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }
    }

    /// <summary>
    /// What the merge does with a file.
    /// </summary>
    private enum OutcomeKind
    {
        /// <summary>Both sides have the same content already.</summary>
        Unchanged,

        /// <summary>The file was not changed locally; the incoming change is taken as it is.</summary>
        TakeIncoming,

        /// <summary>Both sides changed the file and the changes were combined.</summary>
        Merged,

        /// <summary>Both sides changed the same lines; the file holds conflict markers.</summary>
        MergedWithMarkers,

        /// <summary>Git cannot merge the file.</summary>
        CannotMerge
    }

    /// <summary>
    /// The outcome of the merge for one file.
    /// </summary>
    /// <param name="Kind">What the merge does with the file.</param>
    /// <param name="Change">The change that writes a merged file, or <see langword="null"/>.</param>
    /// <param name="File">The temporary file with the merged content, or <see langword="null"/>.</param>
    /// <param name="Reason">Why the file cannot be merged, as an encoded message, or <see langword="null"/>.</param>
    private sealed record Outcome(OutcomeKind Kind, FileChange? Change, string? File, string? Reason)
    {
        /// <summary>Gets the outcome of a file both sides already agree on.</summary>
        public static Outcome Unchanged { get; } = new(OutcomeKind.Unchanged, null, null, null);

        /// <summary>Gets the outcome of a file that takes the incoming change as it is.</summary>
        public static Outcome TakeIncoming { get; } = new(OutcomeKind.TakeIncoming, null, null, null);

        /// <summary>
        /// Creates the outcome of a file Git cannot merge.
        /// </summary>
        /// <param name="reason">The reason, as an encoded message.</param>
        /// <returns>The outcome.</returns>
        public static Outcome CannotMerge(string reason) => new(OutcomeKind.CannotMerge, null, null, reason);
    }

    /// <summary>
    /// The content the transaction writes: the merged files from the temporary directory, everything else from the patch.
    /// </summary>
    private sealed class MergedContent : IPatchContent
    {
        /// <summary>The content of the patch.</summary>
        private readonly IPatchContent _incoming;

        /// <summary>The merged files by the path of their change.</summary>
        private readonly IReadOnlyDictionary<string, string> _merged;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergedContent"/> class.
        /// </summary>
        /// <param name="incoming">The content of the patch.</param>
        /// <param name="merged">The merged files by the path of their change.</param>
        public MergedContent(IPatchContent incoming, IReadOnlyDictionary<string, string> merged)
        {
            _incoming = incoming;
            _merged = merged;
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
            => _merged.TryGetValue(change.Path, out var file)
                ? Task.FromResult<Stream>(File.OpenRead(file))
                : _incoming.OpenReadAsync(change, cancellationToken);
    }
}
