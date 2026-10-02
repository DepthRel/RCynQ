using System.Text;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Localization;
using RCynQ.Sync.Content;

namespace RCynQ.Sync.Apply;

/// <summary>
/// Brings the sync base of the sender in line with a patch the peer has applied.
/// </summary>
/// <remarks>
/// <para>
/// The sender streams files from its working tree, so a file can change between the moment its hash was put in the
/// manifest and the moment the base is updated. The base must describe exactly what the peer received, therefore an added or
/// modified file is copied only when the working tree still has the hash from the manifest. Otherwise the base keeps its old
/// state for that file, and the next patch sends the newer version: nothing the user typed is ever lost from the diff. That next
/// patch names the old base content, so the peer, which has the manifest version, asks the user to resolve it instead of
/// overwriting silently.
/// </para>
/// <para>
/// Deletions are applied as they are.
/// </para>
/// </remarks>
public sealed class SyncBaseUpdater
{
    /// <summary>
    /// The applier that writes the files.
    /// </summary>
    private readonly ContentOverwriteApplier _applier;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBaseUpdater"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="applier">The applier that writes the files.</param>
    /// <param name="paths">The path utility.</param>
    public SyncBaseUpdater(ILocalizer localizer, ContentOverwriteApplier applier, IPathUtility paths)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _applier = applier;
        _paths = paths;
    }

    /// <summary>
    /// Copies the files of a delivered patch from the working tree into the sync base.
    /// </summary>
    /// <param name="envelope">The delivered patch.</param>
    /// <param name="workdir">The working tree the files were sent from.</param>
    /// <param name="syncBase">The sync base to update.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entries written into the sync base; the others were left unchanged because the working tree changed after the manifest was built.</returns>
    public async Task<IReadOnlyList<FileChange>> UpdateAsync(PatchEnvelope envelope, string workdir, string syncBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var stable = new List<FileChange>();
        foreach (var change in envelope.Manifest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.Kind == ChangeKind.Deleted || await StillMatchesAsync(change, workdir, cancellationToken))
            {
                stable.Add(change);
            }
        }

        var accepted = new PatchEnvelope(
            envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, stable, envelope.Signature, envelope.Repository);
        await _applier.ApplyAsync(accepted, new WorkdirPatchContent(_localizer, workdir, _paths), syncBase, cancellationToken);
        return stable;
    }

    /// <summary>
    /// Checks that a file of the working tree still has the hash announced in the manifest.
    /// </summary>
    /// <param name="change">The manifest entry.</param>
    /// <param name="workdir">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the file is unchanged.</returns>
    private async Task<bool> StillMatchesAsync(FileChange change, string workdir, CancellationToken cancellationToken)
    {
        var full = _paths.ResolveWithinRoot(workdir, change.Path);
        if (change.IsSymlink)
        {
            var target = new FileInfo(full).LinkTarget ?? new DirectoryInfo(full).LinkTarget;
            return target is not null && string.Equals(DefaultStateHasher.Sha256Hex(Encoding.UTF8.GetBytes(target)), change.Sha256, StringComparison.Ordinal);
        }

        if (!File.Exists(full) || new FileInfo(full).LinkTarget is not null)
        {
            return false;
        }

        return string.Equals(await DefaultStateHasher.Sha256FileAsync(full, cancellationToken), change.Sha256, StringComparison.Ordinal);
    }
}
