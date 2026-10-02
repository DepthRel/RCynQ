using System.Text;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Cli.Services;

/// <summary>
/// Runs the Git channel commands.
/// </summary>
public sealed class GitCommandService
{
    /// <summary>The Git channel.</summary>
    private readonly GitChannelService _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandService"/> class.
    /// </summary>
    /// <param name="git">The Git channel.</param>
    public GitCommandService(GitChannelService git)
    {
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
            text.Append("Branch ").Append(branch);
            text.Append(info.TrackingRef is null
                ? " has no counterpart on Origin.\n"
                : $": {info.Ahead} ahead, {info.Behind} behind {info.TrackingRef}.\n");
        }

        return (text.Length == 0 ? "Fetch completed.\n" : text.ToString(), result.Succeeded);
    }
}
