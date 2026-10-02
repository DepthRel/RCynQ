using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
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
/// Serves the branches of the Dest repository to a Source that asks for them, as an incremental git bundle.
/// </summary>
/// <remarks>
/// <para>
/// This is the "pull" of a Dest that runs no SSH server: the Source names the commits it already has, and the bundle carries
/// only what is new, delta-compressed by Git. It reaches the Source through the same endpoint and the same chain as patches.
/// </para>
/// <para>
/// The request is authorized like a patch: the link token and a known Source peer. It gives read access to the repository, nothing else.
/// The bundle is written to a temporary file of the node and deleted when the response is done. Only one bundle is built at a time.
/// </para>
/// </remarks>
public sealed class GitBundleService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

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
    public GitBundleService(ILocalizer localizer, NodeConfig node, IPeerRepository peers, LinkTokenProvider tokens, IGitRepositoryService git, IAppDataFactory appData)
    {
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
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bundle, or <see langword="null"/> when the peer has everything.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The request is not allowed.</exception>
    /// <exception cref="SyncBridgeException">The node has no repository to serve.</exception>
    public async Task<GitBundleFile?> CreateAsync(IReadOnlyList<string> have, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(have);
        ArgumentNullException.ThrowIfNull(context);
        await AuthorizeAsync(context, cancellationToken);

        var repository = _node.RepoPath;
        if (repository is null || !await _git.IsWorkTreeAsync(repository, cancellationToken))
        {
            throw new SyncBridgeException(_localizer.Format("Git.BundleNoRepo", repository ?? string.Empty));
        }

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
    /// Checks the role, the link token and the sender of a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is allowed.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    private async Task AuthorizeAsync(PatchReceiveContext context, CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Dest)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, _localizer.Format("Git.BundleOnlyDest", _node.Role));
        }

        if (!LinkTokenProvider.Matches(await _tokens.GetTokenAsync(cancellationToken), context.LinkToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidToken, _localizer.Get("Receive.InvalidToken"));
        }

        if (!NodeId.TryParse(context.SenderNodeId, out var sender)
            || await _peers.GetAsync(sender, cancellationToken) is not { Role: NodeRole.Source })
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
