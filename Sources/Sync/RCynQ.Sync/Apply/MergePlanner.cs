using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Localization;
using RCynQ.Sync.Enums;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Apply;

/// <summary>
/// Works out the three-way merge of one file of an incoming patch in a temporary directory, with the sync base as the
/// common ancestor, without touching the working tree.
/// </summary>
/// <remarks>
/// <para>
/// <c>git merge-file</c> takes the local file, the sync base and the incoming file, needs no repository and no clean
/// working tree, and leaves the usual conflict markers where both sides changed the same lines.
/// </para>
/// <para>
/// The line endings of the local file win. The sync base and the incoming file are brought to them before the merge;
/// otherwise a file checked out with CRLF on one node and LF on the other would collide on every line.
/// </para>
/// <para>
/// The copies the merge starts from stay next to the merged file, so a merge tool can be given the same three versions
/// Git merged.
/// </para>
/// </remarks>
public sealed class MergePlanner
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

    /// <summary>
    /// Initializes a new instance of the <see cref="MergePlanner"/> class.
    /// </summary>
    /// <param name="git">The Git command runner.</param>
    /// <param name="paths">The path utility.</param>
    public MergePlanner(IGitCommandRunner git, IPathUtility paths)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Works out what the merge does with one file.
    /// </summary>
    /// <param name="index">The position of the change in the patch; it names the temporary files.</param>
    /// <param name="change">The change.</param>
    /// <param name="content">The source of the incoming content.</param>
    /// <param name="ancestor">The sync base.</param>
    /// <param name="root">The working tree.</param>
    /// <param name="scratch">The temporary directory of the merge.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The plan for the file.</returns>
    /// <exception cref="SyncBridgeException">Git could not be started.</exception>
    public async Task<MergePlan> PlanAsync(int index, FileChange change, IPatchContent content, string ancestor, string root, string scratch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(content);
        var local = _paths.ResolveWithinRoot(root, change.Path);
        var common = _paths.ResolveWithinRoot(ancestor, change.Path);
        if (Directory.Exists(local) && new DirectoryInfo(local).LinkTarget is null)
        {
            return new MergePlan(MergePlanKind.CannotMerge, FileState.DIRECTORY_MARKER, Reason: LocalizedText.Encode("Conflict.DirectoryLocally"));
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
            return new MergePlan(MergePlanKind.Unchanged, oursHash);
        }

        if (oursHash == await FileState.ContentHashAsync(common, cancellationToken))
        {
            return new MergePlan(MergePlanKind.TakeIncoming, oursHash);
        }

        if (change.Kind == ChangeKind.Deleted)
        {
            return new MergePlan(MergePlanKind.CannotMerge, oursHash, Reason: LocalizedText.Encode("Merge.DeletedBySender"));
        }

        if (oursHash is null)
        {
            return new MergePlan(MergePlanKind.CannotMerge, oursHash, Reason: LocalizedText.Encode("Merge.DeletedLocally"));
        }

        if (change.IsSymlink || new FileInfo(local).LinkTarget is not null)
        {
            return new MergePlan(MergePlanKind.CannotMerge, oursHash, Reason: LocalizedText.Encode("Merge.Link"));
        }

        var ours = Path.Combine(scratch, FormattableString.Invariant($"{index}.ours"));
        var @base = Path.Combine(scratch, FormattableString.Invariant($"{index}.base"));
        var merged = Path.Combine(scratch, FormattableString.Invariant($"{index}.merged"));
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
            return new MergePlan(MergePlanKind.CannotMerge, oursHash, Reason: LocalizedText.Encode("Merge.Binary"));
        }

        await AdoptLineEndingsAsync(ours, new[] { @base, theirs }, cancellationToken);

        // The merge is written into a copy of the local file, so the local version itself stays for a merge tool.
        File.Copy(ours, merged);
        var result = await _git.RunAsync(
            new[] { "merge-file", "-L", LOCAL_LABEL, "-L", BASE_LABEL, "-L", INCOMING_LABEL, Path.GetFileName(merged), Path.GetFileName(@base), Path.GetFileName(theirs) },
            scratch,
            cancellationToken: cancellationToken);
        if (result.ExitCode is < 0 or > MAX_CONFLICT_EXIT_CODE)
        {
            return new MergePlan(MergePlanKind.CannotMerge, oursHash, Reason: LocalizedText.Encode("Merge.Failed", result.StandardError.Trim()));
        }

        var mergedChange = new FileChange(ChangeKind.Modified, change.Path, await DefaultStateHasher.Sha256FileAsync(merged, cancellationToken), new FileInfo(merged).Length, false);
        return new MergePlan(
            result.ExitCode == 0 ? MergePlanKind.Merged : MergePlanKind.MergedWithMarkers,
            oursHash,
            mergedChange,
            merged,
            ours,
            @base,
            theirs);
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
}
