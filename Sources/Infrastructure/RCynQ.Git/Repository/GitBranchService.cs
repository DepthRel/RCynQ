using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Repository;

/// <summary>
/// Default <see cref="IGitBranchService"/>.
/// </summary>
public sealed class GitBranchService : IGitBranchService
{
    /// <summary>The prefix of local branches.</summary>
    private const string HEADS = "refs/heads/";

    /// <summary>The environment that keeps the messages of Git in English, the only language the refusal is read in.</summary>
    private static readonly IReadOnlyDictionary<string, string> _englishMessages = new Dictionary<string, string>
    {
        ["LC_ALL"] = "C",
        ["LANGUAGE"] = "C"
    };

    /// <summary>The git command runner.</summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBranchService"/> class.
    /// </summary>
    /// <param name="git">The git command runner.</param>
    public GitBranchService(IGitCommandRunner git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitSwitchableBranch>> ListSwitchableAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        GitRepositoryService.ValidateRemoteName(remoteName);

        var remotePrefix = $"refs/remotes/{remoteName}/";
        var refs = await _git.RunAsync(new[] { "for-each-ref", "--format=%(objectname) %(refname)", HEADS, remotePrefix }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(refs, "git for-each-ref");

        var local = new Dictionary<string, string>(StringComparer.Ordinal);
        var remote = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, commit) in GitRepositoryService.ParseRefList(refs.StandardOutput))
        {
            if (name.StartsWith(HEADS, StringComparison.Ordinal) && GitNames.IsBranch(name[HEADS.Length..]))
            {
                local[name[HEADS.Length..]] = commit;
                continue;
            }

            // HEAD of the namespace is the record of the branch the Origin has checked out, not a branch.
            var remoteBranch = name.StartsWith(remotePrefix, StringComparison.Ordinal) ? name[remotePrefix.Length..] : null;
            if (remoteBranch is not null && remoteBranch != "HEAD" && GitNames.IsBranch(remoteBranch))
            {
                remote[remoteBranch] = commit;
            }
        }

        var branches = new List<GitSwitchableBranch>();
        foreach (var (name, commit) in local)
        {
            if (!remote.TryGetValue(name, out var remoteCommit))
            {
                branches.Add(new GitSwitchableBranch(name, BranchSyncStatus.LocalOnly, 0, 0));
                continue;
            }

            var (ahead, behind) = commit == remoteCommit
                ? (0, 0)
                : await CountDivergenceAsync(directory, HEADS + name, remotePrefix + name, cancellationToken);
            branches.Add(new GitSwitchableBranch(name, BranchSyncStatus.Pulled, ahead, behind));
        }

        branches.AddRange(remote.Keys
            .Where(name => !local.ContainsKey(name))
            .Select(name => new GitSwitchableBranch(name, BranchSyncStatus.NotPulled, 0, 0)));

        return branches.OrderBy(branch => branch.Name, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<GitSwitchAttempt> SwitchAsync(string directory, string remoteName, string branch, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        GitRepositoryService.ValidateRemoteName(remoteName);
        if (!GitNames.IsBranch(branch))
        {
            throw new ArgumentException($"Branch name '{branch}' may contain only letters, digits, '.', '_', '/' and '-'.", nameof(branch));
        }

        var tracking = $"refs/remotes/{remoteName}/{branch}";
        var hasLocal = await RefExistsAsync(directory, HEADS + branch, cancellationToken);
        if (!hasLocal && !await RefExistsAsync(directory, tracking, cancellationToken))
        {
            throw new ArgumentException($"Neither a local branch nor {remoteName}/{branch} exists.", nameof(branch));
        }

        // The name passed the check above, so it cannot be read as an option; "git switch" takes no paths, so it is no path either.
        var arguments = hasLocal
            ? new[] { "switch", "--quiet", "--no-guess", branch }
            : new[] { "switch", "--quiet", "--no-track", "-c", branch, tracking };
        var result = await _git.RunInWorkingTreeAsync(arguments, directory, _englishMessages, cancellationToken);

        var output = (result.StandardOutput + result.StandardError).Trim();
        return result.ExitCode == 0
            ? new GitSwitchAttempt(true, !hasLocal, Array.Empty<string>(), output)
            : new GitSwitchAttempt(false, false, ReadBlockingFiles(result.StandardError), output);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCommitsOnlyInHeadAsync(string directory, int max, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        var detached = await _git.RunAsync(new[] { "symbolic-ref", "--quiet", "HEAD" }, directory, cancellationToken: cancellationToken);
        if (detached.ExitCode == 0 || !await RefExistsAsync(directory, "HEAD", cancellationToken))
        {
            return Array.Empty<string>();
        }

        var result = await _git.RunAsync(
            new[] { "rev-list", "--max-count=" + max, "HEAD", "--not", "--branches", "--remotes", "--tags" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(result, "git rev-list");
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(commit => GitNames.IsCommit(commit))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<int> CountCommitsNotOnRemoteAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        GitRepositoryService.ValidateRemoteName(remoteName);
        if (!await RefExistsAsync(directory, "HEAD", cancellationToken))
        {
            return 0;
        }

        var result = await _git.RunAsync(new[] { "rev-list", "--count", "HEAD", "--not", "--remotes=" + remoteName }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0 && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    /// <inheritdoc />
    public async Task SetRemoteHeadAsync(string directory, string remoteName, string? branch, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        GitRepositoryService.ValidateRemoteName(remoteName);

        var head = $"refs/remotes/{remoteName}/HEAD";
        var target = $"refs/remotes/{remoteName}/{branch}";
        if (GitNames.IsBranch(branch) && branch != "HEAD" && await RefExistsAsync(directory, target, cancellationToken))
        {
            var set = await _git.RunAsync(new[] { "symbolic-ref", "-m", "rcynq: checked out on Origin", head, target }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(set, "git symbolic-ref");
            return;
        }

        if (await _git.RunAsync(new[] { "symbolic-ref", "--quiet", head }, directory, cancellationToken: cancellationToken) is { ExitCode: 0 })
        {
            var delete = await _git.RunAsync(new[] { "symbolic-ref", "--delete", head }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(delete, "git symbolic-ref --delete");
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetRemoteHeadAsync(string directory, string remoteName, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        GitRepositoryService.ValidateRemoteName(remoteName);

        var prefix = $"refs/remotes/{remoteName}/";
        var result = await _git.RunAsync(new[] { "symbolic-ref", "--quiet", prefix + "HEAD" }, directory, cancellationToken: cancellationToken);
        var target = result.StandardOutput.Trim();
        if (result.ExitCode != 0 || !target.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var branch = target[prefix.Length..];
        return GitNames.IsBranch(branch) ? branch : null;
    }

    /// <summary>
    /// Counts the commits each of two refs has and the other has not.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="left">The first ref.</param>
    /// <param name="right">The second ref.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commits only in <paramref name="left"/> and only in <paramref name="right"/>; zeros when Git cannot tell.</returns>
    private async Task<(int Ahead, int Behind)> CountDivergenceAsync(string directory, string left, string right, CancellationToken cancellationToken)
    {
        var counts = await _git.RunAsync(new[] { "rev-list", "--left-right", "--count", $"{left}...{right}" }, directory, cancellationToken: cancellationToken);
        var parts = counts.StandardOutput.Split(new[] { '\t', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return counts.ExitCode == 0 && parts.Length == 2 && int.TryParse(parts[0], out var ahead) && int.TryParse(parts[1], out var behind)
            ? (ahead, behind)
            : (0, 0);
    }

    /// <summary>
    /// Checks whether a ref names a commit.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="reference">The full ref name, or <c>HEAD</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the ref exists.</returns>
    private async Task<bool> RefExistsAsync(string directory, string reference, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", reference + "^{commit}" }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Reads the files Git names when it refuses to switch: the indented lines under "would be overwritten by checkout".
    /// </summary>
    /// <param name="error">The error output of Git, in English.</param>
    /// <returns>The files, relative to the repository.</returns>
    private static IReadOnlyList<string> ReadBlockingFiles(string error)
        => error.Split('\n')
            .Where(line => line.StartsWith('\t'))
            .Select(line => line.Trim().Trim('"'))
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
}
