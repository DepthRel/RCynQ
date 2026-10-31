using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.MergeTools;

/// <summary>
/// Reads which merge tool the user chose for Git, so RCynQ offers the same one first.
/// </summary>
/// <remarks>
/// Only <c>merge.tool</c> and <c>mergetool.&lt;name&gt;.path</c> are read. <c>mergetool.&lt;name&gt;.cmd</c> is never
/// read: it is a line for a shell, and running it would run any command written there.
/// </remarks>
public sealed partial class GitMergeToolHint
{
    /// <summary>How long Git may take to answer.</summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    /// <summary>Starts Git.</summary>
    private readonly IProcessRunner _processes;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeToolHint"/> class.
    /// </summary>
    /// <param name="processes">Starts Git.</param>
    public GitMergeToolHint(IProcessRunner processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <summary>
    /// Reads the setting.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting for Git.</param>
    /// <returns>The setting, or <see langword="null"/> when no tool is chosen or Git cannot be asked.</returns>
    public async Task<GitMergeToolSetting?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var name = await ReadValueAsync("merge.tool", cancellationToken);
        if (name is null || !ToolName().IsMatch(name))
        {
            // A name Git could not use in mergetool.<name>.path is not a name of a tool.
            return null;
        }

        var path = await ReadValueAsync($"mergetool.{name}.path", cancellationToken);
        return new GitMergeToolSetting(name, path);
    }

    /// <summary>
    /// Reads one value of the global Git configuration.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">A token to stop waiting for Git.</param>
    /// <returns>The value, or <see langword="null"/> when it is not set or Git cannot be asked.</returns>
    private async Task<string?> ReadValueAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processes.RunAsync(new ProcessSpec("git", new[] { "config", "--global", "--get", key }, null, null, _timeout), cancellationToken);
            var value = result.StandardOutput.Trim();
            return result.ExitCode == 0 && value.Length > 0 ? value : null;
        }
        catch (SyncBridgeException)
        {
            // Git is missing or did not answer: the tools are still offered, only without a preference.
            return null;
        }
    }

    /// <summary>
    /// Matches a name of a tool Git accepts in a key of its configuration.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();
}
