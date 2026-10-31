using System.Globalization;
using System.Text;
using RCynQ.Cli.Output;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Services;

namespace RCynQ.Cli.Services;

/// <summary>
/// Sends, lists and resolves patches, saves and applies patch files and creates the baseline.
/// </summary>
public sealed class PatchCommandService
{
    /// <summary>The patch service.</summary>
    private readonly IPatchService _patches;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="patches">The patch service.</param>
    public PatchCommandService(ILocalizer localizer, IPatchService patches)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _patches = patches;
    }

    /// <summary>
    /// Sends the local changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the patch reached a successful state (or nothing had to be sent).</returns>
    public async Task<(string Text, bool Succeeded)> SendAsync(CancellationToken cancellationToken = default)
    {
        var result = await _patches.SendAsync(null, cancellationToken);
        var text = new StringBuilder(result.Message).Append('\n');
        if (result.PatchId is { } id)
        {
            text.Append("Patch: ").Append(id).Append('\n');
        }

        if (result.Result is { HasConflicts: true } applyResult)
        {
            AppendConflicts(text, applyResult);
        }

        var succeeded = !result.Sent || result.Result?.Status is PatchStatus.Applied or PatchStatus.ConflictPending or PatchStatus.Pending;
        return (text.ToString(), succeeded);
    }

    /// <summary>
    /// Lists recent patches.
    /// </summary>
    /// <param name="take">The maximum number of patches.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The table.</returns>
    public async Task<string> ListAsync(int take, CancellationToken cancellationToken = default)
    {
        var patches = await _patches.GetRecentAsync(Math.Clamp(take, 1, 500), cancellationToken);
        if (patches.Count == 0)
        {
            return "No patches yet.\n";
        }

        var table = new ConsoleTable("Created", "Direction", "From", "To", "Status", "Files", "Conflicts", "Id");
        foreach (var patch in patches)
        {
            table.AddRow(
                TextFormat.Time(patch.CreatedAtUtc),
                patch.Direction.ToString(),
                patch.SourceNode.Value,
                patch.TargetNode.Value,
                patch.Status.ToString(),
                patch.Manifest.Count.ToString(CultureInfo.InvariantCulture),
                patch.Conflicts.Count.ToString(CultureInfo.InvariantCulture),
                patch.Id.ToString());
        }

        return table.Render();
    }

    /// <summary>
    /// Resolves a patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="policy">The conflict policy.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the resolution succeeded without remaining conflicts.</returns>
    public async Task<(string Text, bool Succeeded)> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        var result = await _patches.ResolveAsync(patchId, policy, cancellationToken);
        var text = new StringBuilder($"Patch {patchId}: {result.Status}");
        if (result.AppliedFileCount > 0)
        {
            text.Append(", ").Append(result.AppliedFileCount).Append(" file(s) written");
        }

        text.Append('\n');
        if (result.Error is not null)
        {
            text.Append("Error: ").Append(LocalizedText.Render(result.Error, _localizer)).Append('\n');
        }

        AppendConflicts(text, result);
        return (text.ToString(), result.Status is PatchStatus.ConflictResolved or PatchStatus.Rejected);
    }

    /// <summary>
    /// Takes a manual snapshot as the sync base, for a working tree without Git.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    public async Task<string> BaselineAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _patches.RebuildBaselineAsync(cancellationToken);
        return $"Sync base rebuilt; state {TextFormat.Hash(snapshot.StateHash)}.\n";
    }

    /// <summary>
    /// Saves the changes of the working tree to a patch file and takes them out when asked to, only once the file is written.
    /// </summary>
    /// <param name="file">The path of the patch file.</param>
    /// <param name="paths">The files to save, or <see langword="null"/> for every changed file.</param>
    /// <param name="discard">Whether the changes leave the working tree together with the patch.</param>
    /// <param name="force">Whether an existing file is replaced.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether everything that was asked for happened.</returns>
    /// <exception cref="SyncBridgeException">The file exists already, or the node cannot build the patch.</exception>
    public async Task<(string Text, bool Succeeded)> SaveFileAsync(string file, IReadOnlyCollection<string>? paths, bool discard, bool force, CancellationToken cancellationToken = default)
    {
        if (File.Exists(file) && !force)
        {
            throw new SyncBridgeException(_localizer.Format("PatchFile.FileExists", file));
        }

        var patch = await _patches.BuildPatchFileAsync(paths, cancellationToken);
        if (patch.Length == 0)
        {
            return (_localizer.Get("SavePatch.Nothing") + "\n", true);
        }

        await File.WriteAllBytesAsync(file, patch, cancellationToken);
        if (!discard)
        {
            return (_localizer.Format("SavePatch.Saved", file) + "\n", true);
        }

        var removed = await _patches.ApplyPatchFileAsync(patch, null, reverse: true, cancellationToken);
        return removed.Succeeded
            ? (_localizer.Format("SavePatch.SavedAndRemoved", file) + "\n", true)
            : (_localizer.Format("SavePatch.SavedNotRemoved", file, removed.Message), false);
    }

    /// <summary>
    /// Lists the files a patch file changes.
    /// </summary>
    /// <param name="file">The path of the patch file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One line per file with the marker of the change.</returns>
    public async Task<string> ListFileAsync(string file, CancellationToken cancellationToken = default)
    {
        var files = await _patches.ReadPatchFileAsync(await File.ReadAllBytesAsync(file, cancellationToken), cancellationToken);

        var text = new StringBuilder();
        foreach (var entry in files)
        {
            text.Append(ChangeMarker(entry.Kind)).Append(' ').Append(entry.Path).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Brings a patch file into the working tree without committing it.
    /// </summary>
    /// <param name="file">The path of the patch file.</param>
    /// <param name="paths">The files of the patch to use, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome, in the words of the program and of Git.</returns>
    public async Task<PatchFileApplyResult> ApplyFileAsync(string file, IReadOnlyCollection<string>? paths, CancellationToken cancellationToken = default)
        => await _patches.ApplyPatchFileAsync(await File.ReadAllBytesAsync(file, cancellationToken), paths, reverse: false, cancellationToken);

    /// <summary>
    /// Gets the one-letter marker of a change, as <c>git status</c> shows it.
    /// </summary>
    /// <param name="kind">The kind of the change.</param>
    /// <returns>The marker.</returns>
    private static char ChangeMarker(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => 'A',
        ChangeKind.Deleted => 'D',
        _ => 'M'
    };

    /// <summary>
    /// Appends the conflict list.
    /// </summary>
    /// <param name="text">The builder.</param>
    /// <param name="result">The result.</param>
    private void AppendConflicts(StringBuilder text, PatchApplyResult result)
    {
        if (!result.HasConflicts)
        {
            return;
        }

        text.Append("Conflicts:\n");
        foreach (var conflict in result.Conflicts)
        {
            text.Append("  ").Append(conflict.Path);
            if (conflict.Detail is not null)
            {
                text.Append(" - ").Append(LocalizedText.Render(conflict.Detail, _localizer));
            }

            text.Append('\n');
        }
    }
}
