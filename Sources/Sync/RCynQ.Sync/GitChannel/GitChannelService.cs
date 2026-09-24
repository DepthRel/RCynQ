using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

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
/// what is new, and the bundle is fetched from a file.
/// </para>
/// <para>
/// Either way the first fetch into a directory without commits makes it a clone of the branch Dest has checked out, so a Source needs
/// nothing but a Pull to start: the sync base is then built from that commit (see <see cref="SyncBaseManager"/>).
/// </para>
/// <para>
/// Branches land in <c>refs/remotes/dest/*</c>; nothing is ever pushed. A <c>dest</c> remote is kept configured (with a placeholder URL
/// RCynQ never uses) purely so external Git tooling recognizes those refs as belonging to a remote; see
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
    /// How many files a refused Pull names before it only counts the rest.
    /// </summary>
    private const int MaxListedFiles = 20;

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

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChannelService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="ssh">The SSH command builder.</param>
    /// <param name="bundles">The client that asks Dest for a bundle.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    public GitChannelService(
        NodeConfig node,
        IChainLinkRepository links,
        IGitRepositoryService git,
        SshCommandBuilder ssh,
        GitBundleClient bundles,
        IAppDataFactory appData,
        SyncBaseManager syncBase,
        SyncPaths paths,
        IPathUtility pathUtility,
        WorkingTreeLock workingTreeLock)
    {
        _node = node;
        _links = links;
        _git = git;
        _ssh = ssh;
        _bundles = bundles;
        _appData = appData;
        _syncBase = syncBase;
        _paths = paths;
        _pathUtility = pathUtility;
        _lock = workingTreeLock;
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
    /// Fetches all branches from Dest. A directory without commits becomes a clone of the branch Dest has checked out.
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
    /// Fetches from Dest and fast-forwards the current branch to the one of Dest, then rebuilds the sync base from the new commit.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; failures are reported in the result, and nothing in the working tree changes then.</returns>
    /// <remarks>
    /// Local changes to the files the Pull updates are handled before Git sees them: a file that already has the content of Dest's
    /// commit, or that was already delivered to Dest by a patch, is replaced by Dest's version. Any other change is one the user
    /// has not sent yet; the Pull is refused and lists those files, so nothing typed on Source is ever overwritten.
    /// </remarks>
    public async Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(cancellationToken);
        if (!fetch.Succeeded)
        {
            return fetch;
        }

        var workdir = _node.EffectiveWorkdir!;
        try
        {
            string output;
            using (await _lock.AcquireAsync(cancellationToken))
            {
                output = await FastForwardAsync(workdir, cancellationToken);
            }

            return new GitOperationResult(true, $"{fetch.Output}\n{output}".Trim(), await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken));
        }
        catch (PullRefusedException ex)
        {
            return new GitOperationResult(false, $"{fetch.Output}\n{ex.Message}".Trim(), await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new GitOperationResult(false, ex.Message, fetch.Branch);
        }
    }

    /// <summary>
    /// Brings the current branch to the commit of Dest; the caller holds the <see cref="WorkingTreeLock"/>.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="PullRefusedException">The Pull cannot be done without losing local work.</exception>
    private async Task<string> FastForwardAsync(string workdir, CancellationToken cancellationToken)
    {
        // The base as it is before the Pull says which local files the peer already has.
        var syncBase = await _syncBase.EnsureAsync(cancellationToken);
        var info = await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken);
        if (info.Branch is null)
        {
            throw new PullRefusedException(Texts.Get("Git.PullDetached"));
        }

        if (info.TrackingRef is null)
        {
            throw new PullRefusedException(Texts.Format("Git.PullNoDestBranch", info.Branch));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        var target = await _git.ResolveCommitAsync(workdir, info.TrackingRef, cancellationToken)
            ?? throw new PullRefusedException(Texts.Format("Git.PullNoDestBranch", info.Branch));
        var lines = new List<string>();
        if (head == target)
        {
            lines.Add(Texts.Format("Git.PullUpToDate", info.Branch));
        }
        else
        {
            if (head is null || !await _git.IsAncestorAsync(workdir, head, target, cancellationToken))
            {
                throw new PullRefusedException(Texts.Format("Git.PullDiverged", info.Branch, info.Ahead, info.Behind));
            }

            var replaced = await ClearDeliveredChangesAsync(workdir, head, target, syncBase.IsReady, cancellationToken);
            if (replaced.Count > 0)
            {
                lines.Add(Texts.Format("Git.PullReplacedDelivered", DescribeFiles(replaced)));
            }

            lines.Add(await _git.PullFastForwardAsync(workdir, RemoteName, cancellationToken));
        }

        var after = await _syncBase.EnsureAsync(cancellationToken);
        if (after.Mode == SyncBaseMode.Git && after.Commit is not null)
        {
            lines.Add(Texts.Format("Git.PullSyncBase", after.Commit[..Math.Min(12, after.Commit.Length)]));
        }

        return string.Join('\n', lines.Where(line => line.Length > 0));
    }

    /// <summary>
    /// Discards the local changes the Pull would otherwise refuse to overwrite, when they are safe to lose.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="head">The current commit.</param>
    /// <param name="target">The commit of Dest.</param>
    /// <param name="baseReady">Whether the sync base can tell which files were delivered to Dest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files whose local changes were discarded.</returns>
    /// <exception cref="PullRefusedException">A changed file has local work that is not on Dest yet; nothing was discarded.</exception>
    private async Task<IReadOnlyList<string>> ClearDeliveredChangesAsync(string workdir, string head, string target, bool baseReady, CancellationToken cancellationToken)
    {
        var updated = (await _git.GetChangedPathsAsync(workdir, head, target, cancellationToken))
            .Select(change => change.Path)
            .ToHashSet(StringComparer.Ordinal);
        var blocking = (await _git.GetDirtyPathsAsync(workdir, cancellationToken)).Where(updated.Contains).ToArray();
        var safe = new List<string>();
        var unsent = new List<string>();
        foreach (var path in blocking)
        {
            if (await _git.MatchesCommitAsync(workdir, target, path, cancellationToken)
                || (baseReady && await IsDeliveredAsync(workdir, path, cancellationToken)))
            {
                safe.Add(path);
            }
            else
            {
                unsent.Add(path);
            }
        }

        if (unsent.Count > 0)
        {
            throw new PullRefusedException(Texts.Format("Git.PullLocalChanges", DescribeFiles(unsent)));
        }

        if (safe.Count > 0)
        {
            await _git.DiscardLocalChangesAsync(workdir, safe, cancellationToken);
        }

        return safe;
    }

    /// <summary>
    /// Checks whether the local version of a file is the one the sync base records as delivered to Dest.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="path">The relative path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when Dest already has exactly this content (or this deletion).</returns>
    private async Task<bool> IsDeliveredAsync(string workdir, string path, CancellationToken cancellationToken)
    {
        try
        {
            var local = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(workdir, path), cancellationToken);
            var delivered = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(_paths.SyncBase, path), cancellationToken);
            return local != FileState.DirectoryMarker && local == delivered;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lists files for a message, one per line, cutting a long list short.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <returns>The text.</returns>
    private static string DescribeFiles(IReadOnlyCollection<string> paths)
    {
        var shown = paths.Take(MaxListedFiles).Select(path => "  " + path);
        var text = string.Join('\n', shown);
        return paths.Count > MaxListedFiles ? $"{text}\n  {Texts.Format("Git.PullMoreFiles", paths.Count - MaxListedFiles)}" : text;
    }

    /// <summary>
    /// Fetches from a Dest that runs an SSH server, over git-over-ssh.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="route">The route to Dest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The git output.</returns>
    /// <exception cref="SyncBridgeException">The Dest peer has no repository path, or has no checked out branch to start from.</exception>
    private async Task<string> FetchSshAsync(string workdir, ChainRoute route, CancellationToken cancellationToken)
    {
        var repoPath = route.Endpoint.Peer.GitRepoPath
            ?? throw new SyncBridgeException(Texts.Format("Git.DestNoRepo", route.Endpoint.Peer.Id));
        var url = GitRepositoryService.BuildSshUrl(route.Target!, repoPath);
        var sshCommand = _ssh.BuildGitSshCommand(route.Jumps);
        var hadCommits = await _git.HasCommitsAsync(workdir, cancellationToken);
        string? destHead = null;
        if (!hadCommits)
        {
            Directory.CreateDirectory(workdir);
            destHead = await _git.GetRemoteHeadBranchAsync(workdir, url, sshCommand, cancellationToken);
            if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
            {
                await _git.InitializeAsync(workdir, destHead ?? "main", cancellationToken);
            }
        }

        var output = await _git.FetchAsync(workdir, url, RemoteName, sshCommand, cancellationToken);
        await _git.EnsureRemoteAsync(workdir, RemoteName, RemotePlaceholderUrl, cancellationToken);
        return hadCommits ? output : await AdoptAsync(workdir, destHead, output, cancellationToken);
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
            return hadCommits ? output : await AdoptAsync(workdir, download.Head, output, cancellationToken);
        }
        finally
        {
            DeleteQuietly(download.Path);
        }
    }

    /// <summary>
    /// Starts a repository without commits on the branch Dest has checked out.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="branch">The branch Dest has checked out, or <see langword="null"/> for a detached HEAD.</param>
    /// <param name="output">The output of the fetch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="SyncBridgeException">Dest has no checked out branch.</exception>
    private async Task<string> AdoptAsync(string workdir, string? branch, string output, CancellationToken cancellationToken)
    {
        if (branch is null)
        {
            throw new SyncBridgeException(Texts.Get("Git.BundleNoHead"));
        }

        var keepFiles = HasWorkingFiles(workdir);
        using (await _lock.AcquireAsync(cancellationToken))
        {
            await _git.AdoptRemoteBranchAsync(workdir, RemoteName, branch, keepFiles, cancellationToken);
        }

        return $"{output}\n{Texts.Format(keepFiles ? "Git.BundleAdoptedKeepingFiles" : "Git.BundleAdopted", branch)}".Trim();
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

    /// <summary>
    /// Signals that a Pull was refused before it changed anything; the message tells the user what to do.
    /// </summary>
    private sealed class PullRefusedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PullRefusedException"/> class.
        /// </summary>
        /// <param name="message">The message for the user.</param>
        public PullRefusedException(string message)
            : base(message)
        {
        }
    }
}
