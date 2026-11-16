using System.Text;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Cli.Services;

/// <summary>
/// Runs the Git channel commands.
/// </summary>
public sealed class GitCommandService
{
    /// <summary>The Git channel.</summary>
    private readonly GitChannelService _git;

    /// <summary>The localizer of the output.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Switches the working tree to another branch.</summary>
    private readonly BranchSwitchService _switcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="git">The Git channel.</param>
    /// <param name="switcher">Switches the working tree to another branch.</param>
    public GitCommandService(ILocalizer localizer, GitChannelService git, BranchSwitchService switcher)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _git = git;
        _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
    }

    /// <summary>
    /// Lists the branches the working tree can be switched to, marking the current one, with how each stands to the Origin.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One line per branch, and the branch the Origin has checked out.</returns>
    public async Task<string> ListBranchesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _switcher.ListAsync(cancellationToken);
        if (list.Branches.Count == 0)
        {
            return _localizer.Get("Cli.Git.NoBranches") + "\n";
        }

        var width = list.Branches.Max(branch => branch.Name.Length);
        var text = new StringBuilder();
        foreach (var branch in list.Branches)
        {
            var status = new List<string>
            {
                _localizer.Get(branch.Status switch
                {
                    BranchSyncStatus.Pulled => "Branch.Status.Pulled",
                    BranchSyncStatus.NotPulled => "Branch.Status.NotPulled",
                    _ => "Branch.Status.LocalOnly"
                })
            };
            if (branch.Ahead > 0 || branch.Behind > 0)
            {
                status.Add(_localizer.Format("Branch.Status.AheadBehind", branch.Ahead, branch.Behind));
            }

            text.Append(branch.Name == list.Current ? "* " : "  ")
                .Append(branch.Name.PadRight(width))
                .Append("  ")
                .Append(string.Join(" · ", status))
                .Append('\n');
        }

        if (list.OriginHead is not null)
        {
            text.Append(_localizer.Format("Cli.Git.OriginHead", list.OriginHead)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Switches the working tree to a branch. Without a choice, a working tree with changes is left as it is and the
    /// changed files are listed.
    /// </summary>
    /// <param name="branch">The branch.</param>
    /// <param name="carry">Whether uncommitted changes are carried over to the new branch.</param>
    /// <param name="savePatch">The file the uncommitted changes are saved to before they are taken out, or <see langword="null"/>.</param>
    /// <param name="force">Whether an existing patch file is replaced.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the working tree is on the branch now.</returns>
    /// <exception cref="SyncBridgeException">Both choices were given, or the patch file exists already.</exception>
    public async Task<(string Text, bool Succeeded)> SwitchAsync(string branch, bool carry, string? savePatch, bool force, CancellationToken cancellationToken = default)
    {
        if (carry && savePatch is not null)
        {
            throw new SyncBridgeException(_localizer.Get("Cli.Git.SwitchOptionsConflict"));
        }

        string? savedTo = null;
        SwitchBranchResult result;
        if (savePatch is not null)
        {
            var patch = await _switcher.BuildChangesPatchAsync(cancellationToken);
            if (patch.Length > 0)
            {
                if (File.Exists(savePatch) && !force)
                {
                    throw new SyncBridgeException(_localizer.Format("PatchFile.FileExists", savePatch));
                }

                await File.WriteAllBytesAsync(savePatch, patch, cancellationToken);
                savedTo = savePatch;
            }

            result = await _switcher.SwitchAsync(new SwitchBranchRequest(branch, savedTo is null ? BranchSwitchMode.IfClean : BranchSwitchMode.SavePatch, savedTo is null ? null : patch), cancellationToken);
        }
        else
        {
            result = await _switcher.SwitchAsync(new SwitchBranchRequest(branch, carry ? BranchSwitchMode.CarryChanges : BranchSwitchMode.IfClean), cancellationToken);
        }

        var message = result.Outcome == BranchSwitchOutcome.ChangesInTheWay
            ? _localizer.Format("Cli.Git.SwitchNeedsChoice", branch, FileList(result.Files))
            : result.Message;
        if (savedTo is not null)
        {
            message = $"{message}\n{_localizer.Format("BranchSwitch.SavedTo", savedTo)}";
        }

        return (message.TrimEnd() + "\n", result.Outcome == BranchSwitchOutcome.Switched);
    }

    /// <summary>
    /// Lists files one per line, indented.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>The text.</returns>
    private static string FileList(IEnumerable<string> files) => string.Join('\n', files.Select(file => "  " + file));

    /// <summary>
    /// Fetches branches from Origin.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the fetch succeeded.</returns>
    public async Task<(string Text, bool Succeeded)> FetchAsync(CancellationToken cancellationToken = default)
    {
        var result = await _git.FetchAsync(cancellationToken);
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            text.Append(result.Output.Trim()).Append('\n');
        }

        if (result.Branch is { Branch: { } branch } info)
        {
            text.Append(info.TrackingRef is null
                ? _localizer.Format("Cli.Git.BranchAlone", branch)
                : _localizer.Format("Cli.Git.BranchDivergence", branch, info.Ahead, info.Behind, info.TrackingRef)).Append('\n');
        }

        return (text.Length == 0 ? _localizer.Get("Cli.Git.FetchDone") + "\n" : text.ToString(), result.Succeeded);
    }
}
