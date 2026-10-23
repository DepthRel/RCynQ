using System.Text.RegularExpressions;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
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
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRepositoryService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="git">The git command runner.</param>
    public GitRepositoryService(ILocalizer localizer, IGitCommandRunner git)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
        var environment = SshEnvironment(sshCommand);
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
            throw new GitExecutionException(_localizer.Format("Git.NotFetched", remoteName));
        }

        var result = await _git.RunInWorkingTreeAsync(new[] { "merge", "--ff-only", info.TrackingRef }, directory, cancellationToken: cancellationToken);
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
            throw new GitExecutionException(_localizer.Format("Git.RevListOutput", counts.StandardOutput.Trim()));
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
    /// Builds an <c>ssh://</c> URL for a repository on an Origin peer.
    /// </summary>
    /// <param name="endpoint">The SSH endpoint of Origin.</param>
    /// <param name="repoPath">The absolute repository path on Origin.</param>
    /// <returns>The URL, for example <c>ssh://git@origin:22/srv/repo</c>.</returns>
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
        var allowed = url.StartsWith("ssh://", StringComparison.Ordinal) ||
            url.StartsWith("file://", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(url);
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
    /// Builds the environment that makes Git reach a remote with the given SSH command.
    /// </summary>
    /// <param name="sshCommand">The SSH command, or <see langword="null"/> to leave the choice to Git.</param>
    /// <returns>The environment variables, or <see langword="null"/> when there is no command.</returns>
    /// <remarks>
    /// Git guesses from the first word of the command which options the client takes. The command may start with a change
    /// of directory, so the guess is replaced by the answer: it is OpenSSH.
    /// </remarks>
    private static Dictionary<string, string>? SshEnvironment(string? sshCommand)
        => sshCommand is null
            ? null
            : new Dictionary<string, string> { ["GIT_SSH_COMMAND"] = sshCommand, ["GIT_SSH_VARIANT"] = "ssh" };

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
        foreach (var commit in commits.Where(commit => commit is not null && CommitName().IsMatch(commit)).Distinct(StringComparer.Ordinal).Take(MAX_KNOWN_COMMITS))
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
    public async Task<IReadOnlyList<GitBranchTip>> ListBranchesAsync(string directory, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));

        // for-each-ref only reads: the repository of an Origin stays untouched.
        var result = await _git.RunAsync(
            new[] { "for-each-ref", "--format=%(objectname) %(refname:strip=2)", "refs/heads" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(result, "git for-each-ref");

        return ParseRefList(result.StandardOutput)
            .Select(entry => new GitBranchTip(entry.Name, entry.Commit))
            .OrderBy(branch => branch.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<GitRemoteBranchChanges> MirrorRemoteBranchesAsync(string directory, string remoteName, IReadOnlyList<GitBranchTip> branches, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteName(remoteName);
        ArgumentNullException.ThrowIfNull(branches);

        var prefix = $"refs/remotes/{remoteName}/";
        var current = await _git.RunAsync(new[] { "for-each-ref", "--format=%(objectname) %(refname)", prefix }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(current, "git for-each-ref");
        var tracked = ParseRefList(current.StandardOutput)
            .Where(entry => entry.Name.StartsWith(prefix, StringComparison.Ordinal) && entry.Name != prefix + "HEAD")
            .ToDictionary(entry => entry.Name[prefix.Length..], entry => entry.Commit, StringComparer.Ordinal);

        // Every name the other repository has, also the ones not safe to pass on: those are never removed here either.
        var listed = branches.Where(branch => branch?.Name is not null).Select(branch => branch.Name).ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        var moved = new List<string>();
        foreach (var branch in branches)
        {
            if (branch?.Name is not { } name ||
                branch.Commit is not { } target ||
                !IsSafeBranchName(name) ||
                !CommitName().IsMatch(target) ||
                (tracked.TryGetValue(name, out var commit) && commit == target))
            {
                continue;
            }

            // A commit the bundle did not bring yet stays for the next fetch; Git itself refuses a malformed ref name.
            var exists = await _git.RunAsync(new[] { "cat-file", "-e", target + "^{commit}" }, directory, cancellationToken: cancellationToken);
            if (exists.ExitCode != 0)
            {
                continue;
            }

            var update = await _git.RunAsync(new[] { "update-ref", "-m", "rcynq: branches of Origin", prefix + name, target }, directory, cancellationToken: cancellationToken);
            if (update.ExitCode == 0)
            {
                (tracked.ContainsKey(name) ? moved : added).Add(name);
            }
        }

        var removed = new List<string>();
        foreach (var name in tracked.Keys.Where(name => !listed.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
        {
            var delete = await _git.RunAsync(new[] { "update-ref", "--no-deref", "-d", prefix + name }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(delete, "git update-ref -d");
            removed.Add(name);
        }

        return new GitRemoteBranchChanges(added, moved, removed);
    }

    /// <inheritdoc />
    public async Task<string?> TrackRemoteBranchAsync(string directory, string remoteName, string branch, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteName(remoteName);
        ValidateBranchName(branch);

        var mergeRef = "refs/heads/" + branch;
        var remote = await ReadConfigAsync(directory, $"branch.{branch}.remote", cancellationToken);
        var merge = await ReadConfigAsync(directory, $"branch.{branch}.merge", cancellationToken);
        if (remote == remoteName && merge == mergeRef)
        {
            return null;
        }

        EnsureSuccess(await _git.RunAsync(new[] { "config", $"branch.{branch}.remote", remoteName }, directory, cancellationToken: cancellationToken), "git config");
        EnsureSuccess(await _git.RunAsync(new[] { "config", $"branch.{branch}.merge", mergeRef }, directory, cancellationToken: cancellationToken), "git config");

        if (remote is null)
        {
            return string.Empty;
        }

        const string HEADS = "refs/heads/";
        return merge is not null && merge.StartsWith(HEADS, StringComparison.Ordinal) ? $"{remote}/{merge[HEADS.Length..]}" : remote;
    }

    /// <summary>
    /// Reads one value of the configuration of a repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The value, or <see langword="null"/> when it is not set.</returns>
    private async Task<string?> ReadConfigAsync(string directory, string key, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(new[] { "config", "--get", key }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Reads the lines "&lt;commit&gt; &lt;name&gt;" that <c>git for-each-ref</c> writes, skipping anything else.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The names with their commits.</returns>
    private static IEnumerable<(string Name, string Commit)> ParseRefList(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0 && CommitName().IsMatch(line[..space]) && line.Length > space + 1)
            {
                yield return (line[(space + 1)..], line[..space]);
            }
        }
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
    public async Task<bool> RenameRemoteAsync(string directory, string oldName, string newName, string placeholderUrl, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteName(oldName);
        ValidateRemoteName(newName);
        ValidateRemoteUrl(placeholderUrl);

        var target = await _git.RunAsync(new[] { "remote", "get-url", newName }, directory, cancellationToken: cancellationToken);
        if (target.ExitCode == 0)
        {
            return false;
        }

        var remote = await _git.RunAsync(new[] { "remote", "get-url", oldName }, directory, cancellationToken: cancellationToken);
        if (remote.ExitCode != 0)
        {
            var branches = await _git.RunAsync(new[] { "for-each-ref", "--count=1", "--format=%(refname)", $"refs/remotes/{oldName}/" }, directory, cancellationToken: cancellationToken);
            if (branches.ExitCode != 0 || string.IsNullOrWhiteSpace(branches.StandardOutput))
            {
                return false;
            }

            // "git remote rename" moves the branches only of a remote that is configured.
            EnsureSuccess(await _git.RunAsync(new[] { "remote", "add", oldName, placeholderUrl }, directory, cancellationToken: cancellationToken), "git remote add");
        }

        EnsureSuccess(await _git.RunAsync(new[] { "remote", "rename", oldName, newName }, directory, cancellationToken: cancellationToken), "git remote rename");
        return true;
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
            throw new GitExecutionException(_localizer.Format("Git.NotFetched", remoteName));
        }

        var head = await _git.RunAsync(new[] { "symbolic-ref", "HEAD", "refs/heads/" + branch }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(head, "git symbolic-ref");
        var reset = await _git.RunInWorkingTreeAsync(new[] { "reset", "--quiet", keepWorkingFiles ? "--mixed" : "--hard", tracking }, directory, cancellationToken: cancellationToken);
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

        // A private index filled with the commit and checked out under a prefix gives exactly the files a checkout of that commit
        // writes: the same line endings, filters and attributes as the user's working tree. (git archive does not: it ignores the
        // line-ending settings RCynQ overrides and honours export-ignore / export-subst, which a checkout does not.)
        var indexPath = Path.Combine(Path.GetTempPath(), $"rcynq-export-{Guid.NewGuid():N}.index");
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexPath };
        var prefix = Path.GetFullPath(destinationDirectory).Replace('\\', '/').TrimEnd('/') + "/";
        try
        {
            var read = await _git.RunInWorkingTreeAsync(new[] { "read-tree", treeish }, directory, environment, cancellationToken);
            EnsureSuccess(read, "git read-tree");
            var checkout = await _git.RunInWorkingTreeAsync(new[] { "checkout-index", "--all", "--force", "--prefix=" + prefix }, directory, environment, cancellationToken);
            EnsureSuccess(checkout, "git checkout-index");
        }
        finally
        {
            try
            {
                File.Delete(indexPath);
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

    /// <inheritdoc />
    public async Task<string?> GetMergeBaseAsync(string directory, string first, string second, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(first);
        ValidateTreeish(second);
        var result = await _git.RunAsync(new[] { "merge-base", first, second }, directory, cancellationToken: cancellationToken);
        var commit = result.StandardOutput.Trim();
        return result.ExitCode == 0 && CommitName().IsMatch(commit) ? commit : null;
    }

    /// <inheritdoc />
    public async Task<bool> IsAncestorAsync(string directory, string ancestor, string descendant, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(ancestor);
        ValidateTreeish(descendant);
        var result = await _git.RunAsync(new[] { "merge-base", "--is-ancestor", ancestor, descendant }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDirtyPathsAsync(string directory, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        var result = await _git.RunInWorkingTreeAsync(
            new[] { "status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(result, "git status");

        // With -z every entry is "XY path" followed by NUL; without renames there is no second path, and paths are never quoted.
        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > 3)
            .Select(entry => entry[3..].Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetChangedSinceAsync(string directory, string commit, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(commit);
        var changed = await _git.RunInWorkingTreeAsync(
            new[] { "diff", "--name-only", "--relative", "--no-renames", "--no-ext-diff", "-z", commit, "--" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(changed, "git diff");
        var untracked = await _git.RunInWorkingTreeAsync(
            new[] { "ls-files", "--others", "--exclude-standard", "-z" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(untracked, "git ls-files");

        return (changed.StandardOutput + "\0" + untracked.StandardOutput)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ChangeKind>> GetChangedFilesSinceAsync(string directory, string commit, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateTreeish(commit);
        var changed = await _git.RunInWorkingTreeAsync(
            new[] { "diff", "--name-status", "--relative", "--no-renames", "--no-ext-diff", "-z", commit, "--" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(changed, "git diff");
        var untracked = await _git.RunInWorkingTreeAsync(
            new[] { "ls-files", "--others", "--exclude-standard", "-z" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(untracked, "git ls-files");

        var result = new Dictionary<string, ChangeKind>(StringComparer.Ordinal);

        // With -z and without renames the output is "STATUS NUL path NUL" for every file.
        var fields = changed.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < fields.Length; i += 2)
        {
            var kind = fields[i] switch
            {
                "A" => ChangeKind.Added,
                "D" => ChangeKind.Deleted,
                _ => ChangeKind.Modified
            };
            result[fields[i + 1].Replace('\\', '/')] = kind;
        }

        foreach (var path in untracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            result.TryAdd(path.Replace('\\', '/'), ChangeKind.Added);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> MatchesCommitAsync(string directory, string commit, string path, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        if (!CommitName().IsMatch(commit ?? string.Empty))
        {
            throw new ArgumentException("The commit must be a full object name.", nameof(commit));
        }

        var full = ResolveWithin(directory, path);
        var committed = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", $"{commit}:{path}" }, directory, cancellationToken: cancellationToken);
        var committedBlob = committed.ExitCode == 0 ? committed.StandardOutput.Trim() : null;

        var info = new FileInfo(full);
        if (info.LinkTarget is not null || Directory.Exists(full))
        {
            return false;
        }

        if (!info.Exists)
        {
            return committedBlob is null;
        }

        if (committedBlob is null)
        {
            return false;
        }

        var local = await _git.RunInWorkingTreeAsync(new[] { "hash-object", "--", path }, directory, cancellationToken: cancellationToken);
        return local.ExitCode == 0 && string.Equals(local.StandardOutput.Trim(), committedBlob, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task DiscardLocalChangesAsync(string directory, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ArgumentNullException.ThrowIfNull(paths);
        var resolved = paths.Select(path => (Path: path, Full: ResolveWithin(directory, path))).ToArray();
        foreach (var (path, full) in resolved)
        {
            // ":(literal)" keeps characters such as '*' or '[' in a file name from being read as a pattern.
            var tracked = await _git.RunAsync(new[] { "cat-file", "-e", $"HEAD:{path}" }, directory, cancellationToken: cancellationToken);
            if (tracked.ExitCode == 0)
            {
                var checkout = await _git.RunInWorkingTreeAsync(new[] { "checkout", "--quiet", "HEAD", "--", ":(literal)" + path }, directory, cancellationToken: cancellationToken);
                EnsureSuccess(checkout, "git checkout");
                continue;
            }

            var unstage = await _git.RunAsync(new[] { "rm", "--cached", "--quiet", "--ignore-unmatch", "--", ":(literal)" + path }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(unstage, "git rm --cached");
            if (new DirectoryInfo(full) is { Exists: true, LinkTarget: not null })
            {
                // A link to a directory is removed as a directory: File.Delete refuses it on Windows.
                Directory.Delete(full);
            }
            else if (new FileInfo(full) is { } file && (file.Exists || file.LinkTarget is not null))
            {
                File.Delete(full);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetRemoteHeadBranchAsync(string directory, string remoteUrl, string? sshCommand, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ValidateRemoteUrl(remoteUrl);
        var environment = SshEnvironment(sshCommand);
        var result = await _git.RunAsync(new[] { "ls-remote", "--symref", remoteUrl, "HEAD" }, directory, environment, cancellationToken);
        EnsureSuccess(result, "git ls-remote");

        // The answer starts with "ref: refs/heads/<branch>\tHEAD" when HEAD is a branch.
        const string PREFIX = "ref: refs/heads/";
        var line = result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(entry => entry.StartsWith(PREFIX, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var branch = line[PREFIX.Length..].Split('\t')[0].Trim();
        return SafeBranchName().IsMatch(branch) && !branch.Contains("..", StringComparison.Ordinal) ? branch : null;
    }

    /// <summary>
    /// Resolves a repository-relative path inside the working tree, refusing Git metadata and paths that leave it.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="path">The relative path, with <c>/</c> separators.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException">The path is unsafe or leaves the working tree.</exception>
    private static string ResolveWithin(string directory, string path)
    {
        var relative = Guard.NotBlank(path, nameof(path));
        if (relative.StartsWith('-') || relative.Any(char.IsControl) || Path.IsPathRooted(relative))
        {
            throw new ArgumentException($"'{path}' is not a safe relative path.", nameof(path));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        // The same notion of "Git metadata" and of path comparison as everywhere else: also the 8.3 short name of .git
        // on NTFS, and a case-insensitive file system on macOS.
        var segments = relative.Split('/', '\\');
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, Paths.PathUtility.PlatformComparison) || segments.Any(Paths.PathUtility.IsGitMetadataName))
        {
            throw new ArgumentException($"'{path}' leaves the working tree or points into Git metadata.", nameof(path));
        }

        return full;
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
        if (!IsSafeBranchName(branch))
        {
            throw new ArgumentException($"Branch name '{branch}' may contain only letters, digits, '.', '_', '/' and '-'.", nameof(branch));
        }
    }

    /// <summary>
    /// Checks whether a branch name is safe to pass to Git.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <returns><see langword="true"/> for letters, digits, '.', '_', '/' and '-' without "..".</returns>
    private static bool IsSafeBranchName(string? branch)
        => branch is not null && SafeBranchName().IsMatch(branch) && !branch.Contains("..", StringComparison.Ordinal);

    /// <summary>
    /// The largest number of commits a peer may name as known.
    /// </summary>
    private const int MAX_KNOWN_COMMITS = 200;

    /// <summary>
    /// Matches a branch name that is safe to pass to Git: up to 100 characters of letters, digits and <c>. _ / -</c>,
    /// not starting with a punctuation mark.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._/-]{0,99}$")]
    private static partial Regex SafeBranchName();

    /// <summary>
    /// Matches a full commit name: 40 (SHA-1) or 64 (SHA-256) lower-case hexadecimal digits.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex CommitName();

    /// <summary>
    /// Matches a remote name that is safe to pass to Git: up to 64 characters of letters, digits and <c>. _ -</c>, not
    /// starting with a punctuation mark.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex SafeRemoteName();

    /// <summary>
    /// Pattern for repository paths embedded in ssh URLs.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_./:~+-]+$")]
    private static partial Regex SafeRepoPath();
}
