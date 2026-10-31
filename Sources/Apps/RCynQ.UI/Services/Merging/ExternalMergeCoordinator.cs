using System.Net;
using System.Security.Cryptography;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.MergeTools;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// Resolves a conflicting patch in a merge tool: the service prepares the files, the tool opens them one by one, and the
/// service applies what the user merged.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is written into the working tree until every file is merged. A file counts as merged when the tool changed
/// the result and left no conflict markers in it; otherwise the user decides: look again (a tool that returns at once
/// is saved later), open it again, keep the markers, or give the merge up.
/// </para>
/// <para>
/// There is no time limit on the tool. Giving the merge up stops waiting for the tool without closing it: the window
/// belongs to the user.
/// </para>
/// </remarks>
public sealed class ExternalMergeCoordinator
{
    /// <summary>The opening of a conflict marker of the local side.</summary>
    private const string OURS_MARKER = "<<<<<<<";

    /// <summary>The opening of a conflict marker of the incoming side.</summary>
    private const string THEIRS_MARKER = ">>>>>>>";

    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>Starts the tool.</summary>
    private readonly IMergeToolLauncher _launcher;

    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalMergeCoordinator"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The service API client.</param>
    /// <param name="launcher">Starts the tool.</param>
    public ExternalMergeCoordinator(ILocalizer localizer, IServiceApiClient api, IMergeToolLauncher launcher)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <summary>
    /// Runs the merge.
    /// </summary>
    /// <param name="patchId">The conflicting patch.</param>
    /// <param name="tool">The merge tool.</param>
    /// <param name="progress">Where the merge shows how it goes and asks the user.</param>
    /// <param name="cancellationToken">Cancelled when the user gives the merge up.</param>
    /// <returns>How the merge ended.</returns>
    /// <exception cref="ServiceApiException">The service could not prepare the merge.</exception>
    public async Task<ExternalMergeOutcome> RunAsync(Guid patchId, MergeTool tool, IMergeProgress progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(progress);

        progress.ShowStatus(_localizer.Get("MergeTool.Preparing"));
        var session = await _api.PrepareMergeAsync(patchId, cancellationToken);
        if (session.SessionId is not { } sessionId)
        {
            return new ExternalMergeOutcome(ExternalMergeOutcomeKind.Unmergeable, null, session.Unmergeable, null, Array.Empty<string>());
        }

        try
        {
            var marked = new List<string>();
            for (var index = 0; index < session.Files.Count; index++)
            {
                var file = session.Files[index];
                progress.ShowFile(index + 1, session.Files.Count, file.Path);
                var merged = await MergeFileAsync(tool, file, progress, cancellationToken);
                if (merged is null)
                {
                    await CancelQuietlyAsync(sessionId);
                    return ExternalMergeOutcome.Cancelled;
                }

                if (merged.Value)
                {
                    marked.Add(file.Path);
                }
            }

            return await CompleteAsync(sessionId, tool, marked, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CancelQuietlyAsync(sessionId);
            return ExternalMergeOutcome.Cancelled;
        }
    }

    /// <summary>
    /// Merges one file: opens it in the tool until the user merged it or gave the merge up.
    /// </summary>
    /// <param name="tool">The merge tool.</param>
    /// <param name="file">The file.</param>
    /// <param name="progress">Where the merge asks the user.</param>
    /// <param name="cancellationToken">Cancelled when the user gives the merge up.</param>
    /// <returns>
    /// <see langword="false"/> when the file is merged, <see langword="true"/> when it is taken with conflict markers,
    /// <see langword="null"/> when the user gave the merge up.
    /// </returns>
    private async Task<bool?> MergeFileAsync(MergeTool tool, MergeSessionFile file, IMergeProgress progress, CancellationToken cancellationToken)
    {
        // What the result held before the tool touched it: Git's merge with its markers.
        var prepared = await StampAsync(file.ResultFile, cancellationToken);
        var labels = new MergeToolLabels(
            _localizer.Get("MergeTool.Label.Local"),
            _localizer.Get("MergeTool.Label.Base"),
            _localizer.Get("MergeTool.Label.Incoming"),
            _localizer.Format("MergeTool.Label.Result", Path.GetFileName(file.Path)));
        var files = new MergeToolFiles(file.LocalFile, file.BaseFile, file.IncomingFile, file.ResultFile);

        var open = true;
        var exitCode = 0;
        while (true)
        {
            if (open)
            {
                progress.ShowStatus(_localizer.Format("MergeTool.Waiting", tool.Name));
                var started = await RunToolAsync(tool, files, labels, progress, cancellationToken);
                if (started is null)
                {
                    return null;
                }

                exitCode = started.Value;
            }

            if (!File.Exists(file.ResultFile) ||
                await StampAsync(file.ResultFile, cancellationToken) == prepared ||
                (tool.TrustExitCode && exitCode != 0))
            {
                var choice = await progress.AskAsync(
                    _localizer.Format("MergeTool.NotSaved", file.Path, tool.Name),
                    new[] { MergePromptChoice.CheckAgain, MergePromptChoice.OpenAgain, MergePromptChoice.Cancel });
                if (choice == MergePromptChoice.Cancel)
                {
                    return null;
                }

                open = choice == MergePromptChoice.OpenAgain;

                // The user says the file is saved now: the code of the run before no longer tells anything.
                exitCode = 0;
                continue;
            }

            if (await HasMarkersAsync(file.ResultFile, cancellationToken))
            {
                var choice = await progress.AskAsync(
                    _localizer.Format("MergeTool.MarkersLeft", file.Path),
                    new[] { MergePromptChoice.OpenAgain, MergePromptChoice.KeepMarkers, MergePromptChoice.Cancel });
                switch (choice)
                {
                    case MergePromptChoice.KeepMarkers:
                        return true;
                    case MergePromptChoice.OpenAgain:
                        open = true;
                        continue;
                    default:
                        return null;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Starts the tool and waits for it to exit, offering to try again when it cannot start.
    /// </summary>
    /// <param name="tool">The merge tool.</param>
    /// <param name="files">The files of the merge.</param>
    /// <param name="labels">The titles of the panes.</param>
    /// <param name="progress">Where the merge asks the user.</param>
    /// <param name="cancellationToken">Cancelled when the user gives the merge up; the tool is left open then.</param>
    /// <returns>The exit code of the tool, or <see langword="null"/> when the user gave the merge up.</returns>
    private async Task<int?> RunToolAsync(MergeTool tool, MergeToolFiles files, MergeToolLabels labels, IMergeProgress progress, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                using var process = _launcher.Start(tool, files, labels);

                // Giving up stops this wait only; the window of the tool stays open for the user.
                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode ?? 0;
            }
            catch (SyncBridgeException ex)
            {
                var choice = await progress.AskAsync(
                    _localizer.Format("MergeTool.StartFailed", _localizer.Describe(ex)),
                    new[] { MergePromptChoice.Retry, MergePromptChoice.Cancel });
                if (choice != MergePromptChoice.Retry)
                {
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// Asks the service to apply the merge, offering to try again when it could not write the files.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="tool">The merge tool.</param>
    /// <param name="marked">The files taken with conflict markers.</param>
    /// <param name="progress">Where the merge shows how it goes and asks the user.</param>
    /// <param name="cancellationToken">Cancelled when the user gives the merge up.</param>
    /// <returns>How the merge ended.</returns>
    private async Task<ExternalMergeOutcome> CompleteAsync(Guid sessionId, MergeTool tool, IReadOnlyList<string> marked, IMergeProgress progress, CancellationToken cancellationToken)
    {
        while (true)
        {
            progress.ShowStatus(_localizer.Get("MergeTool.Applying"));
            try
            {
                var result = await _api.CompleteMergeAsync(sessionId, tool.Name, cancellationToken);
                return new ExternalMergeOutcome(ExternalMergeOutcomeKind.Completed, result, Array.Empty<ConflictFile>(), null, marked);
            }
            catch (ServiceApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                // The merge no longer fits the working tree, or the session is gone: the service ended it.
                return ExternalMergeOutcome.Failed(ex.Message);
            }
            catch (ServiceApiException ex)
            {
                var choice = await progress.AskAsync(
                    _localizer.Format("MergeTool.ApplyFailed", ex.Message),
                    new[] { MergePromptChoice.Retry, MergePromptChoice.Cancel });
                if (choice != MergePromptChoice.Retry)
                {
                    await CancelQuietlyAsync(sessionId);
                    return ExternalMergeOutcome.Cancelled;
                }
            }
        }
    }

    /// <summary>
    /// Gives the session up on the service; a session the service cannot remove ends with its lifetime.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <returns>A task that completes when the service answered.</returns>
    private async Task CancelQuietlyAsync(Guid sessionId)
    {
        try
        {
            await _api.CancelMergeAsync(sessionId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException or TaskCanceledException)
        {
            // The service is gone or busy; it removes the session itself later.
        }
    }

    /// <summary>
    /// Takes the fingerprint of a file, to tell whether the tool saved it.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The SHA-256 of the content, or an empty text when the file is missing.</returns>
    private static async Task<string> StampAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>
    /// Tells whether a file still has conflict markers: a line that starts with <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> or
    /// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>. A line of <c>=======</c> alone is no marker: it is a heading in Markdown.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when markers are left.</returns>
    private static async Task<bool> HasMarkersAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith(OURS_MARKER, StringComparison.Ordinal) || line.StartsWith(THEIRS_MARKER, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
