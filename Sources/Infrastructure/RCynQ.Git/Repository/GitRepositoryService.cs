using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Repository;

/// <summary>
/// Default <see cref="IGitRepositoryService"/>.
/// </summary>
public sealed partial class GitRepositoryService : IGitRepositoryService
{
    /// <summary>
    /// The git command runner.
    /// </summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRepositoryService"/> class.
    /// </summary>
    /// <param name="git">The git command runner.</param>
    public GitRepositoryService(IGitCommandRunner git)
    {
        _git = git;
    }

    /// <inheritdoc />
    public async Task<bool> IsWorkTreeAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var result = await _git.RunAsync(new[] { "rev-parse", "--is-inside-work-tree" }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0 && result.StandardOutput.Trim() == "true";
    }

    /// <inheritdoc />
    public async Task<string> FetchAsync(string directory, string remoteUrl, string remoteName, string? sshCommand, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteUrl(remoteUrl);
        ValidateRemoteName(remoteName);
        var environment = sshCommand is null ? null : new Dictionary<string, string> { ["GIT_SSH_COMMAND"] = sshCommand };
        var result = await _git.RunAsync(
            new[] { "fetch", "--prune", "--no-tags", "--no-write-fetch-head", remoteUrl, $"+refs/heads/*:refs/remotes/{remoteName}/*" },
            directory,
            environment,
            cancellationToken);
        EnsureSuccess(result, "git fetch");
        return (result.StandardError + result.StandardOutput).Trim();
    }

    /// <inheritdoc />
    public async Task<string> PullFastForwardAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        var info = await GetBranchInfoAsync(directory, remoteName, cancellationToken);
        if (info.Branch is null || info.TrackingRef is null)
        {
            throw new GitExecutionException($"Current branch has no fetched counterpart under refs/remotes/{remoteName}; run fetch first.");
        }

        var result = await _git.RunAsync(new[] { "merge", "--ff-only", info.TrackingRef }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git merge --ff-only");
        return (result.StandardOutput + result.StandardError).Trim();
    }

    /// <inheritdoc />
    public async Task<GitBranchInfo> GetBranchInfoAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        if (!await IsWorkTreeAsync(directory, cancellationToken))
        {
            return new GitBranchInfo(false, null, null, 0, 0);
        }

        var head = await _git.RunAsync(new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }, directory, cancellationToken: cancellationToken);
        if (head.ExitCode != 0)
        {
            return new GitBranchInfo(true, null, null, 0, 0);
        }

        var branch = head.StandardOutput.Trim();
        var tracking = $"refs/remotes/{remoteName}/{branch}";
        var exists = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", tracking }, directory, cancellationToken: cancellationToken);
        if (exists.ExitCode != 0)
        {
            return new GitBranchInfo(true, branch, null, 0, 0);
        }

        var counts = await _git.RunAsync(new[] { "rev-list", "--left-right", "--count", $"HEAD...{tracking}" }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(counts, "git rev-list");
        var parts = counts.StandardOutput.Split(new[] { '\t', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
        {
            throw new GitExecutionException($"Unexpected git rev-list output: '{counts.StandardOutput.Trim()}'.");
        }

        return new GitBranchInfo(true, branch, tracking, ahead, behind);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUnmergedFilesAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(new[] { "diff", "--name-only", "--relative", "--diff-filter=U" }, directory, cancellationToken: cancellationToken);
        return result.ExitCode != 0
            ? Array.Empty<string>()
            : result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Builds an <c>ssh://</c> URL for a repository on a Dest peer.
    /// </summary>
    /// <param name="endpoint">The SSH endpoint of Dest.</param>
    /// <param name="repoPath">The absolute repository path on Dest.</param>
    /// <returns>The URL, for example <c>ssh://git@dest:22/srv/repo</c>.</returns>
    /// <exception cref="ArgumentException">The repository path contains unsafe characters.</exception>
    public static string BuildSshUrl(SshEndpoint endpoint, string repoPath)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var path = Guard.NotBlank(repoPath, nameof(repoPath)).Replace('\\', '/');
        if (!SafeRepoPath().IsMatch(path))
        {
            throw new ArgumentException($"Repository path '{repoPath}' contains unsupported characters.", nameof(repoPath));
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var host = endpoint.Host.Contains(':') ? $"[{endpoint.Host}]" : endpoint.Host;
        return $"ssh://{endpoint.User}@{host}:{endpoint.Port}{path}";
    }

    /// <summary>
    /// Validates that a remote URL cannot be interpreted as an option or a dangerous transport.
    /// </summary>
    /// <param name="remoteUrl">The URL.</param>
    /// <exception cref="ArgumentException">The URL is unsafe.</exception>
    public static void ValidateRemoteUrl(string remoteUrl)
    {
        var url = Guard.NotBlank(remoteUrl, nameof(remoteUrl));
        var allowed = url.StartsWith("ssh://", StringComparison.Ordinal)
            || url.StartsWith("file://", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(url);
        if (!allowed || url.StartsWith('-') || url.Any(char.IsControl))
        {
            throw new ArgumentException($"Remote URL '{remoteUrl}' is not an allowed ssh://, file:// or absolute path remote.", nameof(remoteUrl));
        }
    }

    /// <summary>
    /// Validates a remote-tracking namespace name.
    /// </summary>
    /// <param name="remoteName">The name.</param>
    /// <exception cref="ArgumentException">The name is unsafe.</exception>
    private static void ValidateRemoteName(string remoteName)
    {
        if (remoteName is null || !SafeRemoteName().IsMatch(remoteName))
        {
            throw new ArgumentException($"Remote name '{remoteName}' may contain only letters, digits, '.', '_' and '-'.", nameof(remoteName));
        }
    }

    /// <summary>
    /// Throws when a git command failed.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="command">The command description.</param>
    /// <exception cref="GitExecutionException">The exit code is not zero.</exception>
    private static void EnsureSuccess(Core.Abstractions.Processes.ProcessResult result, string command)
    {
        if (result.ExitCode != 0)
        {
            throw new GitExecutionException($"{command} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}", result.ExitCode);
        }
    }

    /// <summary>
    /// Pattern for remote names.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex SafeRemoteName();

    /// <summary>
    /// Pattern for repository paths embedded in ssh URLs.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_./:~+-]+$")]
    private static partial Regex SafeRepoPath();
}
