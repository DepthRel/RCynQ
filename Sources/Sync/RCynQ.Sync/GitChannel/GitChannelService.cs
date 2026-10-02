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
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// The Git channel of a Workstation node: branches are fetched only from Origin, through the same chain as patches.
/// </summary>
/// <remarks>
/// <para>
/// Origin is asked for a bundle of the repository this node works with: this node names the repository and the commits
/// it already has, Origin answers with only what is new, and the bundle is fetched from a file. The request travels the
/// way patches do (through the SSH forward or the TLS port of Origin), so Origin needs no Git access over SSH and its
/// repository is only read.
/// </para>
/// <para>
/// The first fetch into a directory without commits makes it a clone of the branch Origin has checked out, so a Workstation needs
/// nothing but a Pull to start: the sync base is then built from that commit (see <see cref="SyncBaseManager"/>).
/// </para>
/// <para>
/// Branches land in <c>refs/remotes/rcynq/*</c>; nothing is ever pushed. A <c>rcynq</c> remote is kept configured (with a placeholder URL
/// RCynQ never uses) purely so external Git tooling recognizes those refs as belonging to a remote; see
/// <see cref="IGitRepositoryService.EnsureRemoteAsync"/>.
/// </para>
/// </remarks>
public sealed class GitChannelService
{
    /// <summary>
    /// The remote-tracking namespace for branches fetched from Origin.
    /// </summary>
    public const string REMOTE_NAME = "rcynq";

    /// <summary>
    /// How many files a refused Pull names before it only counts the rest.
    /// </summary>
    private const int MAX_LISTED_FILES = 20;

    /// <summary>
    /// The placeholder URL stored for the <c>rcynq</c> remote, so ordinary Git tooling recognizes fetched branches as belonging
    /// to a remote (see <see cref="IGitRepositoryService.EnsureRemoteAsync"/>). RCynQ never fetches through it.
    /// </summary>
    internal const string REMOTE_PLACEHOLDER_URL = "file:///RCynQ-managed-remote-use-the-Branches-tab-to-fetch";

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The client that asks Origin for a bundle.</summary>
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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Renames the remote-tracking namespace of an earlier version, or <see langword="null"/>.</summary>
    private readonly LegacyRemoteMigration? _legacyRemote;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChannelService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="bundles">The client that asks Origin for a bundle.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="legacyRemote">Renames the remote-tracking namespace of an earlier version; <see langword="null"/> leaves it alone.</param>
    public GitChannelService(
        ILocalizer localizer,
        NodeConfig node,
        IChainLinkRepository links,
        IGitRepositoryService git,
        GitBundleClient bundles,
        IAppDataFactory appData,
        SyncBaseManager syncBase,
        SyncPaths paths,
        IPathUtility pathUtility,
        WorkingTreeLock workingTreeLock,
        LegacyRemoteMigration? legacyRemote = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _links = links;
        _git = git;
        _bundles = bundles;
        _appData = appData;
        _syncBase = syncBase;
        _paths = paths;
        _pathUtility = pathUtility;
        _lock = workingTreeLock;
        _legacyRemote = legacyRemote;
    }

    /// <summary>
    /// Gets the current branch of the working tree and its divergence from the branches fetched from Origin.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branch information; a non-repository result for a Relay.</returns>
    public async Task<GitBranchInfo> GetBranchAsync(CancellationToken cancellationToken = default)
    {
        if (_paths.Repository?.Path is not { } workdir)
        {
            return new GitBranchInfo(false, null, null, 0, 0);
        }

        await MigrateLegacyRemoteAsync(workdir, cancellationToken);
        return await _git.GetBranchInfoAsync(workdir, REMOTE_NAME, cancellationToken);
    }

    /// <summary>
    /// Renames the remote-tracking namespace an earlier version left in the working copy.
    /// </summary>
    /// <param name="workdir">The working copy.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the working copy was looked at.</returns>
    private Task MigrateLegacyRemoteAsync(string workdir, CancellationToken cancellationToken)
        => _legacyRemote is null ? Task.CompletedTask : _legacyRemote.EnsureAsync(workdir, cancellationToken);

    /// <summary>
    /// Fetches all branches from Origin. A directory without commits becomes a clone of the branch Origin has checked out.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; failures are reported in the result.</returns>
    public async Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (workdir, chain, _) = await ResolveAsync(cancellationToken);
            await MigrateLegacyRemoteAsync(workdir, cancellationToken);
            var output = await FetchBundleAsync(workdir, chain, cancellationToken);
            return new GitOperationResult(true, output, await _git.GetBranchInfoAsync(workdir, REMOTE_NAME, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            // "Failures are reported in the result" includes a working directory or a bundle file that cannot be written.
            return new GitOperationResult(false, _localizer.Describe(ex), null);
        }
    }

    /// <summary>
    /// Fetches from Origin and fast-forwards the current branch to the one of Origin, then rebuilds the sync base from the new commit.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; failures are reported in the result, and nothing in the working tree changes then.</returns>
    /// <remarks>
    /// Local changes to the files the Pull updates are handled before Git sees them: a file that already has the content of Origin's
    /// commit, or that was already delivered to Origin by a patch, is replaced by Origin's version. Any other change is one the user
    /// has not sent yet; the Pull is refused and lists those files, so nothing typed on Workstation is ever overwritten.
    /// </remarks>
    public async Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(cancellationToken);
        if (!fetch.Succeeded)
        {
            return fetch;
        }

        var workdir = _paths.Workdir;
        try
        {
            string output;
            using (await _lock.AcquireAsync(cancellationToken))
            {
                output = await FastForwardAsync(workdir, cancellationToken);
            }

            return new GitOperationResult(true, $"{fetch.Output}\n{output}".Trim(), await _git.GetBranchInfoAsync(workdir, REMOTE_NAME, cancellationToken));
        }
        catch (PullRefusedException ex)
        {
            return new GitOperationResult(false, $"{fetch.Output}\n{_localizer.Describe(ex)}".Trim(), await _git.GetBranchInfoAsync(workdir, REMOTE_NAME, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new GitOperationResult(false, _localizer.Describe(ex), fetch.Branch);
        }
    }

    /// <summary>
    /// Brings the current branch to the commit of Origin; the caller holds the <see cref="WorkingTreeLock"/>.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="PullRefusedException">The Pull cannot be done without losing local work.</exception>
    private async Task<string> FastForwardAsync(string workdir, CancellationToken cancellationToken)
    {
        // The base as it is before the Pull says which local files the peer already has.
        var syncBase = await _syncBase.EnsureAsync(cancellationToken);
        var info = await _git.GetBranchInfoAsync(workdir, REMOTE_NAME, cancellationToken);
        if (info.Branch is null)
        {
            throw new PullRefusedException(_localizer.Get("Git.PullDetached"));
        }

        if (info.TrackingRef is null)
        {
            throw new PullRefusedException(_localizer.Format("Git.PullNoOriginBranch", info.Branch));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        var target = await _git.ResolveCommitAsync(workdir, info.TrackingRef, cancellationToken)
            ?? throw new PullRefusedException(_localizer.Format("Git.PullNoOriginBranch", info.Branch));
        var lines = new List<string>();
        if (head == target)
        {
            lines.Add(_localizer.Format("Git.PullUpToDate", info.Branch));
        }
        else
        {
            if (head is null || !await _git.IsAncestorAsync(workdir, head, target, cancellationToken))
            {
                throw new PullRefusedException(_localizer.Format("Git.PullDiverged", info.Branch, info.Ahead, info.Behind));
            }

            var replaced = await ClearDeliveredChangesAsync(workdir, head, target, syncBase.IsReady, cancellationToken);
            if (replaced.Count > 0)
            {
                lines.Add(_localizer.Format("Git.PullReplacedDelivered", DescribeFiles(replaced)));
            }

            lines.Add(await _git.PullFastForwardAsync(workdir, REMOTE_NAME, cancellationToken));
        }

        var after = await _syncBase.EnsureAsync(cancellationToken);
        if (after.Mode == SyncBaseMode.Git && after.Commit is not null)
        {
            lines.Add(_localizer.Format("Git.PullSyncBase", after.Commit[..Math.Min(12, after.Commit.Length)]));
        }

        return string.Join('\n', lines.Where(line => line.Length > 0));
    }

    /// <summary>
    /// Discards the local changes the Pull would otherwise refuse to overwrite, when they are safe to lose.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="head">The current commit.</param>
    /// <param name="target">The commit of Origin.</param>
    /// <param name="baseReady">Whether the sync base can tell which files were delivered to Origin.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files whose local changes were discarded.</returns>
    /// <exception cref="PullRefusedException">A changed file has local work that is not on Origin yet; nothing was discarded.</exception>
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
            if (await _git.MatchesCommitAsync(workdir, target, path, cancellationToken) ||
                (baseReady && await IsDeliveredAsync(workdir, path, cancellationToken)))
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
            throw new PullRefusedException(_localizer.Format("Git.PullLocalChanges", DescribeFiles(unsent)));
        }

        if (safe.Count > 0)
        {
            await _git.DiscardLocalChangesAsync(workdir, safe, cancellationToken);
        }

        return safe;
    }

    /// <summary>
    /// Checks whether the local version of a file is the one the sync base records as delivered to Origin.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="path">The relative path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when Origin already has exactly this content (or this deletion).</returns>
    private async Task<bool> IsDeliveredAsync(string workdir, string path, CancellationToken cancellationToken)
    {
        try
        {
            var local = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(workdir, path), cancellationToken);
            var delivered = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(_paths.SyncBase, path), cancellationToken);
            return local != FileState.DIRECTORY_MARKER && local == delivered;
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
    private string DescribeFiles(IReadOnlyCollection<string> paths)
    {
        var shown = paths.Take(MAX_LISTED_FILES).Select(path => "  " + path);
        var text = string.Join('\n', shown);
        return paths.Count > MAX_LISTED_FILES ? $"{text}\n  {_localizer.Format("Git.PullMoreFiles", paths.Count - MAX_LISTED_FILES)}" : text;
    }

    /// <summary>
    /// Asks Origin for a bundle with what is new in the repository this node works with and fetches it from the file.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="chain">The chain to Origin.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="SyncBridgeException">Origin refused, could not be reached, or has no checked out branch to start from.</exception>
    private async Task<string> FetchBundleAsync(string workdir, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken)
    {
        var hadCommits = await _git.HasCommitsAsync(workdir, cancellationToken);
        var have = hadCommits ? await _git.GetKnownCommitsAsync(workdir, REMOTE_NAME, cancellationToken) : Array.Empty<string>();
        var repository = _paths.Repository is { IsBound: true } bound ? bound.Name : null;
        var download = await _bundles.DownloadAsync(chain, have, _appData.Create().TempDirectory, repository, cancellationToken);
        if (download is null)
        {
            // Nothing new, but the remote must exist regardless of whether this call brought any commits, so external Git
            // tooling can already recognize the branches an earlier pull fetched.
            if (hadCommits)
            {
                await _git.EnsureRemoteAsync(workdir, REMOTE_NAME, REMOTE_PLACEHOLDER_URL, cancellationToken);
            }

            return _localizer.Get("Git.BundleUpToDate");
        }

        try
        {
            if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
            {
                await _git.InitializeAsync(workdir, download.Head ?? "main", cancellationToken);
            }

            var output = await _git.FetchBundleAsync(workdir, download.Path, REMOTE_NAME, cancellationToken);
            await _git.EnsureRemoteAsync(workdir, REMOTE_NAME, REMOTE_PLACEHOLDER_URL, cancellationToken);
            return hadCommits ? output : await AdoptAsync(workdir, download.Head, output, cancellationToken);
        }
        finally
        {
            DeleteQuietly(download.Path);
        }
    }

    /// <summary>
    /// Starts a repository without commits on the branch Origin has checked out.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="branch">The branch Origin has checked out, or <see langword="null"/> for a detached HEAD.</param>
    /// <param name="output">The output of the fetch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A description of the outcome.</returns>
    /// <exception cref="SyncBridgeException">Origin has no checked out branch.</exception>
    private async Task<string> AdoptAsync(string workdir, string? branch, string output, CancellationToken cancellationToken)
    {
        if (branch is null)
        {
            throw new SyncBridgeException(_localizer.Get("Git.BundleNoHead"));
        }

        var keepFiles = HasWorkingFiles(workdir);
        using (await _lock.AcquireAsync(cancellationToken))
        {
            await _git.AdoptRemoteBranchAsync(workdir, REMOTE_NAME, branch, keepFiles, cancellationToken);
        }

        return $"{output}\n{_localizer.Format(keepFiles ? "Git.BundleAdoptedKeepingFiles" : "Git.BundleAdopted", branch)}".Trim();
    }

    /// <summary>
    /// Checks whether a directory holds anything besides the Git metadata.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns><see langword="true"/> when there are files or directories to keep.</returns>
    private static bool HasWorkingFiles(string directory)
        => Directory.Exists(directory) &&
            Directory.EnumerateFileSystemEntries(directory).Any(entry => !string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase));

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
    /// Resolves the working tree, the chain and the route to Origin.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved values.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Workstation or the chain is incomplete.</exception>
    private async Task<(string Workdir, IReadOnlyList<ChainLinkConfig> Chain, ChainRoute Route)> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Workstation)
        {
            throw new SyncBridgeException(_localizer.Format("Git.OnlyWorkstation", _node.Role));
        }

        var chain = await _links.GetAllAsync(cancellationToken);
        try
        {
            return (_paths.Workdir, chain, ChainRoute.Resolve(chain));
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
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
