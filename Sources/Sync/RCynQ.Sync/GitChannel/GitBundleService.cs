using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Security;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Serves the branches of the Origin repository to a Workstation that asks for them, as an incremental git bundle.
/// </summary>
/// <remarks>
/// <para>
/// This is the "pull" of an Origin that runs no SSH server: the Workstation names the commits it already has, and the bundle carries
/// only what is new, delta-compressed by Git. It reaches the Workstation through the same endpoint and the same chain as patches.
/// </para>
/// <para>
/// The request is authorized like a patch: the link token and a known Workstation peer. It gives read access to the repository, nothing else.
/// The bundle is written to a temporary file of the node and deleted when the response is done. Only one bundle is built at a time.
/// </para>
/// </remarks>
public sealed class GitBundleService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The repositories of the node.</summary>
    private readonly IActiveRepository _repositories;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>Serializes the creation of bundles.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBundleService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="repositories">The repositories of the node; <see langword="null"/> for the one the node started with.</param>
    public GitBundleService(ILocalizer localizer, NodeConfig node, IPeerRepository peers, LinkTokenProvider tokens, IGitRepositoryService git, IAppDataFactory appData, IActiveRepository? repositories = null)
    {
        _repositories = repositories ?? FixedRepository.FromNode(node);
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _peers = peers;
        _tokens = tokens;
        _git = git;
        _appData = appData;
    }

    /// <summary>
    /// Creates the bundle for a peer.
    /// </summary>
    /// <param name="have">The commits the peer claims to have; the ones this repository does not know are ignored.</param>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="repositoryName">
    /// The name of the repository asked for: any repository the node registered, not only the one it works with now, since
    /// a bundle only reads. <see langword="null"/> asks for the repository the node works with.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bundle, or <see langword="null"/> when the peer has everything.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The request is not allowed.</exception>
    /// <exception cref="SyncBridgeException">The node has no repository to serve.</exception>
    public async Task<GitBundleFile?> CreateAsync(IReadOnlyList<string> have, PatchReceiveContext context, string? repositoryName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(have);
        ArgumentNullException.ThrowIfNull(context);
        await AuthorizeAsync(context, cancellationToken);
        var repository = await ResolveRepositoryAsync(repositoryName, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var known = await _git.FilterExistingCommitsAsync(repository, have, cancellationToken);
            var directory = _appData.Create().TempDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"bundle-{Guid.NewGuid():N}.bundle");
            try
            {
                if (!await _git.CreateBundleAsync(repository, path, known, cancellationToken))
                {
                    return null;
                }

                return new GitBundleFile(path, await _git.GetCurrentBranchAsync(repository, cancellationToken));
            }
            catch
            {
                Delete(path);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lists every branch of the repository for a peer, so it also learns of the branches a bundle does not carry: a branch
    /// created at a commit the peer has, a branch moved back, a branch that was deleted.
    /// </summary>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="repositoryName">The name of the repository asked for; <see langword="null"/> for the one the node works with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The checked out branch and at most <see cref="ApiLimits.MAX_GIT_BRANCHES"/> branches.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The request is not allowed.</exception>
    /// <exception cref="SyncBridgeException">The node has no repository to serve.</exception>
    public async Task<GitBranchListResponse> ListBranchesAsync(PatchReceiveContext context, string? repositoryName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await AuthorizeAsync(context, cancellationToken);
        var repository = await ResolveRepositoryAsync(repositoryName, cancellationToken);

        var branches = await _git.ListBranchesAsync(repository, cancellationToken);
        return new GitBranchListResponse(
            await _git.GetCurrentBranchAsync(repository, cancellationToken),
            branches.Take(ApiLimits.MAX_GIT_BRANCHES).ToArray());
    }

    /// <summary>
    /// Finds the working tree of the repository a peer asks for.
    /// </summary>
    /// <param name="repositoryName">
    /// The name of the repository: any repository the node registered, not only the one it works with now, since serving
    /// it only reads. <see langword="null"/> names the repository the node works with.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The working tree.</returns>
    /// <exception cref="PatchRejectedException">The node has no repository of that name.</exception>
    /// <exception cref="SyncBridgeException">The repository is not a Git working tree.</exception>
    private async Task<string> ResolveRepositoryAsync(string? repositoryName, CancellationToken cancellationToken)
    {
        var entry = string.IsNullOrWhiteSpace(repositoryName)
            ? _repositories.Current
            : _repositories.Entries.FirstOrDefault(candidate => candidate.IsNamed(repositoryName.Trim()))
                ?? throw new PatchRejectedException(PatchRejectionReason.UnknownRepository, _localizer.Format("Repository.UnknownHere", repositoryName.Trim(), _node.Name));
        var repository = entry?.Path;
        if (repository is null || !await _git.IsWorkTreeAsync(repository, cancellationToken))
        {
            throw new SyncBridgeException(_localizer.Format("Git.BundleNoRepo", repository ?? string.Empty));
        }

        return repository;
    }

    /// <summary>
    /// Checks the role, the link token and the sender of a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is allowed.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    private async Task AuthorizeAsync(PatchReceiveContext context, CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Origin)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, _localizer.Format("Git.BundleOnlyOrigin", _node.Role));
        }

        if (!LinkTokenProvider.Matches(await _tokens.GetTokenAsync(cancellationToken), context.LinkToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidToken, _localizer.Get("Receive.InvalidToken"));
        }

        if (!NodeId.TryParse(context.SenderNodeId, out var sender) ||
            await _peers.GetAsync(sender, cancellationToken) is not { Role: NodeRole.Workstation })
        {
            throw new PatchRejectedException(PatchRejectionReason.UnknownSender, _localizer.Format("Git.BundleUnknownPeer", context.SenderNodeId ?? string.Empty));
        }
    }

    /// <summary>
    /// Deletes a temporary file, ignoring failures.
    /// </summary>
    /// <param name="path">The file.</param>
    private static void Delete(string path)
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
}
