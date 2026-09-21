using RCynQ.Core.Abstractions.Repositories;
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
/// The Git channel of a Source node: branches are fetched only from Dest, through the same SSH chain as patches.
/// </summary>
/// <remarks>
/// The remote URL is built from the Dest peer (<see cref="PeerDescriptor.GitRepoPath"/>), and <c>GIT_SSH_COMMAND</c>
/// carries the jump hosts. Branches land in <c>refs/remotes/dest/*</c>; nothing is ever pushed.
/// </remarks>
public sealed class GitChannelService
{
    /// <summary>
    /// The remote-tracking namespace for branches fetched from Dest.
    /// </summary>
    public const string RemoteName = "dest";

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The SSH command builder.</summary>
    private readonly SshCommandBuilder _ssh;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChannelService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="ssh">The SSH command builder.</param>
    public GitChannelService(NodeConfig node, IChainLinkRepository links, IGitRepositoryService git, SshCommandBuilder ssh)
    {
        _node = node;
        _links = links;
        _git = git;
        _ssh = ssh;
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
            var (workdir, url, sshCommand) = await ResolveAsync(cancellationToken);
            var output = await _git.FetchAsync(workdir, url, RemoteName, sshCommand, cancellationToken);
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
        var fetch = await FetchAsync(cancellationToken);
        if (!fetch.Succeeded)
        {
            return fetch;
        }

        try
        {
            var workdir = _node.EffectiveWorkdir!;
            var output = await _git.PullFastForwardAsync(workdir, RemoteName, cancellationToken);
            return new GitOperationResult(true, $"{fetch.Output}\n{output}".Trim(), await _git.GetBranchInfoAsync(workdir, RemoteName, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return new GitOperationResult(false, ex.Message, fetch.Branch);
        }
    }

    /// <summary>
    /// Resolves the working tree, the remote URL and the SSH command.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved values.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Source or the chain is incomplete.</exception>
    private async Task<(string Workdir, string Url, string SshCommand)> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Source)
        {
            throw new SyncBridgeException(Texts.Format("Git.OnlySource", _node.Role));
        }

        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(await _links.GetAllAsync(cancellationToken));
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(ex.Message, ex);
        }

        if (route.Endpoint.Peer.EndpointMode != EndpointMode.SshTunnel)
        {
            throw new SyncBridgeException(
                Texts.Format("Git.DestNoSsh", route.Endpoint.Peer.Id));
        }

        var repoPath = route.Endpoint.Peer.GitRepoPath
            ?? throw new SyncBridgeException(Texts.Format("Git.DestNoRepo", route.Endpoint.Peer.Id));
        return (_node.EffectiveWorkdir!, GitRepositoryService.BuildSshUrl(route.Target!, repoPath), _ssh.BuildGitSshCommand(route.Jumps));
    }
}
