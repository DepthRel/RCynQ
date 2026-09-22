using System.IO.Compression;
using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;
using RCynQ.Localization;

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
            throw new GitExecutionException(Texts.Format("Git.NotFetched", remoteName));
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
            throw new GitExecutionException(Texts.Format("Git.RevListOutput", counts.StandardOutput.Trim()));
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

    /// <inheritdoc />
    public async Task<string?> GetCurrentBranchAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!await IsWorkTreeAsync(directory, cancellationToken))
        {
            return null;
        }

        var head = await _git.RunAsync(new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }, directory, cancellationToken: cancellationToken);
        return head.ExitCode == 0 ? head.StandardOutput.Trim() : null;
    }

    /// <inheritdoc />
    public async Task<bool> HasCommitsAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!await IsWorkTreeAsync(directory, cancellationToken))
        {
            return false;
        }

        var head = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", "HEAD" }, directory, cancellationToken: cancellationToken);
        return head.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetKnownCommitsAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        if (!await HasCommitsAsync(directory, cancellationToken))
        {
            return Array.Empty<string>();
        }

        var commits = new List<string>();
        var head = await _git.RunAsync(new[] { "rev-parse", "--verify", "HEAD" }, directory, cancellationToken: cancellationToken);
        if (head.ExitCode == 0)
        {
            commits.Add(head.StandardOutput.Trim());
        }

        var refs = await _git.RunAsync(
            new[] { "for-each-ref", "--format=%(objectname)", "refs/heads", $"refs/remotes/{remoteName}" },
            directory,
            cancellationToken: cancellationToken);
        if (refs.ExitCode == 0)
        {
            commits.AddRange(refs.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return commits.Where(commit => CommitName().IsMatch(commit)).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FilterExistingCommitsAsync(string directory, IEnumerable<string> commits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commits);
        var existing = new List<string>();
        foreach (var commit in commits.Where(commit => commit is not null && CommitName().IsMatch(commit)).Distinct(StringComparer.Ordinal).Take(MaxKnownCommits))
        {
            var check = await _git.RunAsync(new[] { "cat-file", "-e", commit + "^{commit}" }, directory, cancellationToken: cancellationToken);
            if (check.ExitCode == 0)
            {
                existing.Add(commit);
            }
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> CreateBundleAsync(string directory, string bundlePath, IReadOnlyCollection<string> have, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        Guard.NotBlank(bundlePath, nameof(bundlePath));
        ArgumentNullException.ThrowIfNull(have);
        var exclusions = have.Select(commit => CommitName().IsMatch(commit) ? "^" + commit : throw new ArgumentException($"'{commit}' is not a commit name.", nameof(have))).ToArray();

        // Git refuses to create an empty bundle and words the refusal in the language of the system, so ask first.
        var probe = await _git.RunAsync(new[] { "rev-list", "--branches", "-n", "1" }.Concat(exclusions).ToArray(), directory, cancellationToken: cancellationToken);
        EnsureSuccess(probe, "git rev-list");
        if (probe.StandardOutput.Trim().Length == 0)
        {
            return false;
        }

        var result = await _git.RunAsync(new[] { "bundle", "create", bundlePath, "--branches" }.Concat(exclusions).ToArray(), directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git bundle create");
        return true;
    }

    /// <inheritdoc />
    public async Task<string> FetchBundleAsync(string directory, string bundlePath, string remoteName, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteUrl(bundlePath);
        ValidateRemoteName(remoteName);
        var result = await _git.RunAsync(
            new[] { "fetch", "--no-tags", "--no-write-fetch-head", bundlePath, $"+refs/heads/*:refs/remotes/{remoteName}/*" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(result, "git fetch");
        return (result.StandardError + result.StandardOutput).Trim();
    }

    /// <inheritdoc />
    public async Task EnsureRemoteAsync(string directory, string remoteName, string placeholderUrl, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteName(remoteName);
        ValidateRemoteUrl(placeholderUrl);

        // A remote already configured (by RCynQ before, or by whoever created the repository) is left exactly as it is:
        // its URL might be a real one a person set up on purpose, and overwriting it would be a surprise.
        var existing = await _git.RunAsync(new[] { "remote", "get-url", remoteName }, directory, cancellationToken: cancellationToken);
        if (existing.ExitCode == 0)
        {
            return;
        }

        var add = await _git.RunAsync(new[] { "remote", "add", remoteName, placeholderUrl }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(add, "git remote add");
    }

    /// <inheritdoc />
    public async Task InitializeAsync(string directory, string initialBranch, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateBranchName(initialBranch);
        Directory.CreateDirectory(directory);
        var result = await _git.RunAsync(new[] { "init", "--quiet", "--initial-branch=" + initialBranch }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git init");
    }

    /// <inheritdoc />
    public async Task AdoptRemoteBranchAsync(string directory, string remoteName, string branch, bool keepWorkingFiles, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        ValidateBranchName(branch);
        var tracking = $"refs/remotes/{remoteName}/{branch}";
        var exists = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", tracking }, directory, cancellationToken: cancellationToken);
        if (exists.ExitCode != 0)
        {
            throw new GitExecutionException(Texts.Format("Git.NotFetched", remoteName));
        }

        var head = await _git.RunAsync(new[] { "symbolic-ref", "HEAD", "refs/heads/" + branch }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(head, "git symbolic-ref");
        var reset = await _git.RunAsync(new[] { "reset", "--quiet", keepWorkingFiles ? "--mixed" : "--hard", tracking }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(reset, "git reset");
    }

    /// <inheritdoc />
    public async Task<string?> GetHeadCommitAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!await HasCommitsAsync(directory, cancellationToken))
        {
            return null;
        }

        var head = await _git.RunAsync(new[] { "rev-parse", "--verify", "HEAD" }, directory, cancellationToken: cancellationToken);
        return head.ExitCode == 0 ? head.StandardOutput.Trim() : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitPathChange>> GetChangedPathsAsync(string directory, string fromCommit, string toCommit, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        if (!CommitName().IsMatch(fromCommit) || !CommitName().IsMatch(toCommit))
        {
            throw new ArgumentException("Both commits must be full object names.");
        }

        var result = await _git.RunAsync(new[] { "diff", "--name-status", "--no-renames", "-z", fromCommit, toCommit }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git diff");

        // With -z the output is "status NUL path NUL status NUL path NUL ...", and paths are never quoted.
        var fields = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitPathChange>();
        for (var i = 0; i + 1 < fields.Length; i += 2)
        {
            changes.Add(new GitPathChange(fields[i].StartsWith('D'), fields[i + 1].Replace('\\', '/')));
        }

        return changes;
    }

    /// <inheritdoc />
    public async Task ExportTreeAsync(string directory, string treeish, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(treeish);
        Guard.NotBlank(destinationDirectory, nameof(destinationDirectory));

        Directory.CreateDirectory(destinationDirectory);
        var zipPath = Path.Combine(Path.GetTempPath(), $"rcynq-export-{Guid.NewGuid():N}.zip");
        try
        {
            var result = await _git.RunAsync(new[] { "archive", "--format=zip", "-o", zipPath, treeish }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(result, "git archive");
            ZipFile.ExtractToDirectory(zipPath, destinationDirectory, overwriteFiles: true);
        }
        finally
        {
            try
            {
                File.Delete(zipPath);
            }
            catch (IOException)
            {
                // Temporary file; a later cleanup removes it.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary file; a later cleanup removes it.
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> ResolveCommitAsync(string directory, string treeish, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(treeish);
        var result = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", treeish + "^{commit}" }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Validates a commit-ish that is passed to Git, so it cannot be read as an option.
    /// </summary>
    /// <param name="treeish">The commit, tag or ref.</param>
    /// <exception cref="ArgumentException">The value is blank, starts with a dash, or contains control characters.</exception>
    private static void ValidateTreeish(string treeish)
    {
        var value = Guard.NotBlank(treeish, nameof(treeish));
        if (value.StartsWith('-') || value.Any(char.IsControl))
        {
            throw new ArgumentException($"'{treeish}' is not a safe commit-ish.", nameof(treeish));
        }
    }

    /// <summary>
    /// Validates a branch name that is passed to Git.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <exception cref="ArgumentException">The name is empty, starts with a dash or contains unusual characters.</exception>
    private static void ValidateBranchName(string branch)
    {
        if (branch is null || !SafeBranchName().IsMatch(branch) || branch.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Branch name '{branch}' may contain only letters, digits, '.', '_', '/' and '-'.", nameof(branch));
        }
    }

    /// <summary>
    /// The largest number of commits a peer may name as known.
    /// </summary>
    private const int MaxKnownCommits = 200;

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._/-]{0,99}$")]
    private static partial Regex SafeBranchName();

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex CommitName();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex SafeRemoteName();

    /// <summary>
    /// Pattern for repository paths embedded in ssh URLs.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_./:~+-]+$")]
    private static partial Regex SafeRepoPath();
}
