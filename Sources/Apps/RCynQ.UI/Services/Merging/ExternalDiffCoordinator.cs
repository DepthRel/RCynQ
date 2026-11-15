using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.MergeTools;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// Shows the diff of a changed file in an external tool, and lets the user choose a part of its changes there: the tool
/// gets a read-only copy of the version of the other node on the left and an editable copy of the version that goes on the
/// right; what the user leaves on the right is what goes.
/// </summary>
public sealed class ExternalDiffCoordinator
{
    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>Starts the tool.</summary>
    private readonly IMergeToolLauncher _launcher;

    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalDiffCoordinator"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The service API client.</param>
    /// <param name="launcher">Starts the tool.</param>
    public ExternalDiffCoordinator(ILocalizer localizer, IServiceApiClient api, IMergeToolLauncher launcher)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <summary>
    /// Opens the diff of a file in the tool and leaves it open; nothing waits for the tool.
    /// </summary>
    /// <param name="path">The path of the file relative to the working tree.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="peerRole">The role of the node the changes go to, named in the titles of the panes.</param>
    /// <param name="cancellationToken">A token to cancel the preparation.</param>
    /// <returns>A task that completes when the tool is started.</returns>
    /// <exception cref="ServiceApiException">The service could not prepare the files.</exception>
    /// <exception cref="Core.Exceptions.SyncBridgeException">The tool could not be started.</exception>
    public async Task ShowAsync(string path, MergeTool tool, NodeRole peerRole, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var files = await _api.PrepareCompareAsync(path, forChoice: false, cancellationToken);
        using var process = _launcher.StartDiff(tool, new DiffToolFiles(files.BaseFile, files.WorkFile), Labels(path, "Diff.Label.Work", peerRole));
    }

    /// <summary>
    /// Lets the user choose a part of the changes of a file in the tool, waits for the tool to close and keeps what the
    /// user left in the right pane.
    /// </summary>
    /// <param name="path">The path of the file relative to the working tree.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="peerRole">The role of the node the changes go to, named in the titles of the panes.</param>
    /// <param name="askOpenAgain">Asks whether to open the tool again when the copy did not change.</param>
    /// <param name="cancellationToken">A token to stop waiting for the tool, which stays open.</param>
    /// <returns>What the choice came to, or <see langword="null"/> when nothing changed.</returns>
    /// <exception cref="ServiceApiException">The service could not prepare or keep the choice.</exception>
    /// <exception cref="Core.Exceptions.SyncBridgeException">The tool could not be started.</exception>
    public async Task<PartialChoiceResult?> ChooseAsync(string path, MergeTool tool, NodeRole peerRole, Func<Task<bool>> askOpenAgain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(askOpenAgain);

        var files = await _api.PrepareCompareAsync(path, forChoice: true, cancellationToken);
        var prepared = await File.ReadAllBytesAsync(files.WorkFile, cancellationToken);
        while (true)
        {
            using (var process = _launcher.StartDiff(tool, new DiffToolFiles(files.BaseFile, files.WorkFile), Labels(path, "Diff.Label.Choice", peerRole)))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            if (await ChangedCopyAsync(files.WorkFile, prepared, cancellationToken) is { } left)
            {
                return await KeepAsync(files, left, cancellationToken);
            }

            // A tool that hands the window to a running instance returns at once, and the user may save the copy while
            // the question is open: the copy is read again after the answer.
            var openAgain = await askOpenAgain();
            if (await ChangedCopyAsync(files.WorkFile, prepared, cancellationToken) is { } saved)
            {
                return await KeepAsync(files, saved, cancellationToken);
            }

            if (!openAgain)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Reads the copy the user edits and tells whether it changed.
    /// </summary>
    /// <param name="file">The copy.</param>
    /// <param name="prepared">What the copy held when it was handed to the tool.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The content of the copy, or <see langword="null"/> when it did not change.</returns>
    private static async Task<byte[]?> ChangedCopyAsync(string file, byte[] prepared, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllBytesAsync(file, cancellationToken);
        return content.AsSpan().SequenceEqual(prepared) ? null : content;
    }

    /// <summary>
    /// Hands the version the user left to the service.
    /// </summary>
    /// <param name="files">The files of the choice.</param>
    /// <param name="content">The version.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>What the choice came to.</returns>
    private Task<PartialChoiceResult> KeepAsync(CompareFilesResponse files, byte[] content, CancellationToken cancellationToken)
        => _api.SetPartialContentAsync(new SetPartialContentRequest(files.Path, files.BaseSha256, files.WorkdirSha256, content), cancellationToken);

    /// <summary>
    /// Builds the titles of the panes.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="rightKey">The key of the title of the right pane.</param>
    /// <param name="peerRole">The role of the node the changes go to.</param>
    /// <returns>The titles.</returns>
    private DiffToolLabels Labels(string path, string rightKey, NodeRole peerRole)
    {
        var name = Path.GetFileName(path);
        return new DiffToolLabels(_localizer.Format("Diff.Label.Base", name, peerRole), _localizer.Format(rightKey, name, peerRole));
    }
}
