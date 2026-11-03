using System.Text;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="git">The Git channel.</param>
    public GitCommandService(ILocalizer localizer, GitChannelService git)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _git = git;
    }

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
