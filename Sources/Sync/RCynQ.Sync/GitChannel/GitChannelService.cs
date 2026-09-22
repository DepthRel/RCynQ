using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Repository;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Routing;
using RCynQ.Localization;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// The Git channel of a Source node: branches are fetched only from Dest, through the same chain as patches.
/// </summary>
/// <remarks>
/// <para>
/// A Dest that runs an SSH server is fetched over git-over-ssh: the remote URL is built from the Dest peer
/// (<see cref="PeerDescriptor.GitRepoPath"/>), and <c>GIT_SSH_COMMAND</c> carries the jump hosts.
/// </para>
/// <para>
/// A Dest that runs no SSH server is asked for a bundle instead: this node names the commits it already has, Dest answers with only
/// what is new, and the bundle is fetched from a file. The first time, a directory without a repository becomes one.
/// </para>
/// <para>
/// Either way branches land in <c>refs/remotes/dest/*</c>; nothing is ever pushed. A <c>dest</c> remote is kept configured (with a
/// placeholder URL RCynQ never uses) purely so external Git tooling recognizes those refs as belonging to a remote; see
/// <see cref="IGitRepositoryService.EnsureRemoteAsync"/>.
/// </para>
/// </remarks>
public sealed class GitChannelService
{
    /// <summary>
    /// The remote-tracking namespace for branches fetched from Dest.
    /// </summary>
    public const string RemoteName = "dest";

    /// <summary>
    /// The placeholder URL stored for the <c>dest</c> remote, so ordinary Git tooling recognizes fetched branches as belonging
    /// to a remote (see <see cref="IGitRepositoryService.EnsureRemoteAsync"/>). RCynQ never fetches through it.
    /// </summary>
    private const string RemotePlaceholderUrl = "file:///RCynQ-managed-remote-use-the-Branches-tab-to-fetch";

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The SSH command builder.</summary>
    private readonly SshCommandBuilder _ssh;

    /// <summary>The client that asks Dest for a bundle.</summary>
    private readonly GitBundleClient _bundles;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The updater of the sync base after a pull.</summary>
    private readonly GitPullBaseUpdater _baseUpdater;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChannelService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="ssh">The SSH command builder.</param>
    /// <param name="bundles">The client that asks Dest for a bundle.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="baseUpdater">The updater of the sync base after a pull.</param>
    public GitChannelService(NodeConfig node, IChainLinkRepository links, IGitRepositoryService git, SshCommandBuilder ssh, GitBundleClient bundles, IAppDataFactory appData, GitPullBaseUpdater baseUpdater)
    {
        _node = node;
        _links = links;
        _git = git;
        _ssh = ssh;
        _bundles = bundles;
        _appData = appData;
        _baseUpdater = baseUpdater;
    }

    /// <summary>
    /// Gets the current branch of the working tree and its divergence from the branches fetched from Dest.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branch information; a non-repository result for a Mediator.</returns>
    public Task<GitBranchInfo> GetBranchAsync(CancellationToken cancellationToken = default)
        => _node.EffectiveWorkdir is { } workdir
            ? _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken)
            : Task.FromResult(new GitBranchInfo(false, null, null, 0, 0));

    /// <summary>
    /// Fetches all branches from Dest.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; failures are reported in the result.</returns>
    public async Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (workdir, chain, route) = await ResolveAsync(cancellationToken);
            var output = route.Endpoint.Peer.EndpointMode == EndpointMode.Direct
                ? await FetchBundleAsync(workdir, chain, cancellationToken)
                : await FetchSshAsync(workdir, route, cancellationToken);
            return new GitOperationResult(true, output, await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return new GitOperationResult(false, ex.Message, null);
        }
    }

    /// <summary>
    /// Fetches from Dest and fast-forwards the current branch.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; failures are reported in the result.</returns>
    public async Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default)
    {
        var before = _node.EffectiveWorkdir is { } directory ? await _git.GetHeadCommitAsync(directory, cancellationToken) : null;
        var fetch = await FetchAsync(cancellationToken);
        if (!fetch.Succeeded)
        {
            return fetch;
        }

        try
        {
            var workdir = _node.EffectiveWorkdir!;
            var output = await _git.PullFastForwardAsync(workdir, RemoteName, cancellationToken);
            var after = await _git.GetHeadCommitAsync(workdir, cancellationToken);
            if (before is not null && after is not null && before != after)
            {
                // The pulled files are now what Dest has, so they are the state both sides share: bring the sync base along.
                var count = _baseUpdater.Update(await _git.GetChangedPathsAsync(workdir, before, after, cancellationToken), workdir);
                if (count > 0)
                {
                    output = $"{output}\n{Texts.Format("Git.PullBaseUpdated", count)}";
                }
            }

            return new GitOperationResult(true, $"{fetch.Output}\n{output}".Trim(), await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return new GitOperationResult(false, ex.Message, fetch.Branch);
        }
    }

    /// <summary>
    /// Fetches from a Dest that runs an SSH server, over git-over-ssh.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="route">The route to Dest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The git output.</returns>
    /// <exception cref="SyncBridgeException">The Dest peer has no repository path.</exception>
    private async Task<string> FetchSshAsync(string workdir, ChainRoute route, CancellationToken cancellationToken)
    {
        var repoPath = route.Endpoint.Peer.GitRepoPath
            ?? throw new SyncBridgeException(Texts.Format("Git.DestNoRepo", route.Endpoint.Peer.Id));
        var url = GitRepositoryService.BuildSshUrl(route.Target!, repoPath);
        var output = await _git.FetchAsync(workdir, url, RemoteName, _ssh.BuildGitSshCommand(route.Jumps), cancellationToken);
        await _git.EnsureRemoteAsync(workdir, RemoteName, RemotePlaceholderUrl, cancellationToken);
        return output;
    }

    /// <summary>
    /// Fetches from a Dest that runs no SSH server: asks for a bundle with what is new and fetches it from the file.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="chain">The chain to Dest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="SyncBridgeException">Dest refused, could not be reached, or has no checked out branch to start from.</exception>
    private async Task<string> FetchBundleAsync(string workdir, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken)
    {
        var hadCommits = await _git.HasCommitsAsync(workdir, cancellationToken);
        var have = hadCommits ? await _git.GetKnownCommitsAsync(workdir, RemoteName, cancellationToken) : Array.Empty<string>();
        var download = await _bundles.DownloadAsync(chain, have, _appData.Create().TempDirectory, cancellationToken);
        if (download is null)
        {
            // Nothing new, but the remote must exist regardless of whether this call brought any commits, so external Git
            // tooling can already recognize the branches an earlier pull fetched.
            if (hadCommits)
            {
                await _git.EnsureRemoteAsync(workdir, RemoteName, RemotePlaceholderUrl, cancellationToken);
            }

            return Texts.Get("Git.BundleUpToDate");
        }

        try
        {
            if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
            {
                await _git.InitializeAsync(workdir, download.Head ?? "main", cancellationToken);
            }

            var output = await _git.FetchBundleAsync(workdir, download.Path, RemoteName, cancellationToken);
            await _git.EnsureRemoteAsync(workdir, RemoteName, RemotePlaceholderUrl, cancellationToken);
            if (hadCommits)
            {
                return output;
            }

            // The first fetch into a repository without commits: start on the branch Dest has checked out.
            var branch = download.Head ?? throw new SyncBridgeException(Texts.Get("Git.BundleNoHead"));
            var keepFiles = HasWorkingFiles(workdir);
            await _git.AdoptRemoteBranchAsync(workdir, RemoteName, branch, keepFiles, cancellationToken);
            return $"{output}\n{Texts.Format(keepFiles ? "Git.BundleAdoptedKeepingFiles" : "Git.BundleAdopted", branch)}".Trim();
        }
        finally
        {
            DeleteQuietly(download.Path);
        }
    }

    /// <summary>
    /// Checks whether a directory holds anything besides the Git metadata.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns><see langword="true"/> when there are files or directories to keep.</returns>
    private static bool HasWorkingFiles(string directory)
        => Directory.Exists(directory)
            && Directory.EnumerateFileSystemEntries(directory).Any(entry => !string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Deletes a temporary file, ignoring failures.
    /// </summary>
    /// <param name="path">The file.</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }
    }

    /// <summary>
    /// Resolves the working tree, the chain and the route to Dest.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved values.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Source or the chain is incomplete.</exception>
    private async Task<(string Workdir, IReadOnlyList<ChainLinkConfig> Chain, ChainRoute Route)> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Source)
        {
            throw new SyncBridgeException(Texts.Format("Git.OnlySource", _node.Role));
        }

        var chain = await _links.GetAllAsync(cancellationToken);
        try
        {
            return (_node.EffectiveWorkdir!, chain, ChainRoute.Resolve(chain));
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(ex.Message, ex);
        }
    }
}
