using System.Security.Cryptography;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Commits;
using RCynQ.Localization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Content;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Packs unsent commits of a Workstation into a bundle, delivers them to the Origin and records the outcome.
/// </summary>
/// <remarks>
/// The block always starts with the oldest unsent commit, so the Origin can fast-forward its branch to it. The commits
/// travel as they are: the same hashes, authors and order arrive on the Origin. After the Origin applied them, a Fetch
/// brings <c>refs/remotes/rcynq/&lt;branch&gt;</c> to the same commit, and the sent commits are no longer unsent.
/// </remarks>
public sealed class CommitSendOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The chain repository.</summary>
    private readonly IChainLinkRepository _chain;

    /// <summary>Finds the unsent commits.</summary>
    private readonly UnsentCommitsReader _reader;

    /// <summary>The Git operations of commits.</summary>
    private readonly IGitCommitService _commits;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The sender.</summary>
    private readonly IPatchSender _sender;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The Git channel that fetches from the Origin after a delivery.</summary>
    private readonly GitChannelService _gitChannel;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitSendOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="reader">Finds the unsent commits.</param>
    /// <param name="commits">The Git operations of commits.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="sender">The sender.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="gitChannel">The Git channel that fetches from the Origin after a delivery.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="timeProvider">The clock.</param>
    public CommitSendOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IChainLinkRepository chain,
        UnsentCommitsReader reader,
        IGitCommitService commits,
        ISignatureService signatures,
        IPatchSender sender,
        IPatchRepository patches,
        PatchOutcomeRecorder outcomes,
        WorkingTreeLock workingTreeLock,
        IAppDataFactory appData,
        GitChannelService gitChannel,
        StateTracker tracker,
        TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _chain = chain;
        _reader = reader;
        _commits = commits;
        _signatures = signatures;
        _sender = sender;
        _patches = patches;
        _outcomes = outcomes;
        _lock = workingTreeLock;
        _appData = appData;
        _gitChannel = gitChannel;
        _tracker = tracker;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Sends the unsent commits from the oldest one up to the given one.
    /// </summary>
    /// <param name="upTo">The last commit to send, or <see langword="null"/> to send every unsent commit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; <see cref="SendPatchResult.Sent"/> is <see langword="false"/> when there is nothing to send.</returns>
    /// <exception cref="SyncBridgeException">
    /// The node is not a Workstation, the chain has no route to the Origin, the commits cannot be sent, or the commit to send
    /// up to is not an unsent one.
    /// </exception>
    public async Task<SendPatchResult> ExecuteAsync(string? upTo, CancellationToken cancellationToken = default)
    {
        if (_node.Role != NodeRole.Workstation)
        {
            throw new SyncBridgeException(_localizer.Format("Commits.OnlyWorkstation", _node.Role));
        }

        var links = await _chain.GetAllAsync(cancellationToken);
        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(links);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
        }

        var directory = _appData.Create().TempDirectory;
        Directory.CreateDirectory(directory);
        var bundlePath = Path.Combine(directory, $"commits-{Guid.NewGuid():N}.bundle");
        try
        {
            CommitDelivery delivery;
            using (await _lock.AcquireAsync(cancellationToken))
            {
                var unsent = await _reader.ReadAsync(cancellationToken);
                if (unsent.Problem is not null)
                {
                    throw new SyncBridgeException(unsent.Problem);
                }

                if (unsent.Commits.Count == 0)
                {
                    return new SendPatchResult(false, null, null, _localizer.Get("Commits.NothingToSend"));
                }

                var block = SelectBlock(unsent.Commits, upTo);
                await _commits.CreateBundleAsync(_paths.Workdir, bundlePath, unsent.OriginCommit!, block[^1].Hash, cancellationToken);
                delivery = new CommitDelivery(unsent.Branch!, unsent.OriginCommit!, block, await HashAsync(bundlePath, cancellationToken), new FileInfo(bundlePath).Length);
            }

            return await DeliverAsync(delivery, route, links, bundlePath, cancellationToken);
        }
        finally
        {
            DeleteQuietly(bundlePath);
        }
    }

    /// <summary>
    /// Signs and delivers the commits, records the outcome and, when the Origin applied them, fetches from it.
    /// </summary>
    /// <param name="delivery">The commits.</param>
    /// <param name="route">The route to the Origin.</param>
    /// <param name="links">The chain.</param>
    /// <param name="bundlePath">The bundle file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    private async Task<SendPatchResult> DeliverAsync(CommitDelivery delivery, ChainRoute route, IReadOnlyList<ChainLinkConfig> links, string bundlePath, CancellationToken cancellationToken)
    {
        // The state hashes of a delivery of commits are the commits themselves: the one it is built on and the one it ends with.
        var envelope = new PatchEnvelope(
            Guid.NewGuid(), _node.Id, route.Endpoint.Peer.Id, _timeProvider.GetUtcNow(), delivery.BaseCommit, delivery.TipCommit, Array.Empty<FileChange>(),
            repository: _paths.Repository is { IsBound: true } repository ? repository.Name : null,
            commits: delivery);
        envelope = envelope.WithSignature(await _signatures.SignAsync(PayloadCanonicalizer.Build(envelope), cancellationToken));
        var metadata = PatchMetadata.FromEnvelope(envelope, PatchDirection.Outgoing);
        await _patches.UpsertAsync(metadata, cancellationToken);

        PatchApplyResult result;
        var delivered = false;
        try
        {
            result = await _sender.SendAsync(envelope, new BundlePatchContent(_localizer, delivery, bundlePath), links, cancellationToken);
            delivered = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = PatchApplyResult.Failed(envelope.Id, _localizer.Describe(ex));
        }

        // The Origin has answered; what follows only writes that down, so it is not cut short by a caller that gives up now.
        await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Commits.Sent", delivery.Commits.Count, route.Endpoint.Peer.Id), CancellationToken.None);
        var message = Describe(result, delivery);
        if (result.Status == PatchStatus.Applied)
        {
            var fetch = await _gitChannel.FetchAsync(CancellationToken.None);
            if (!fetch.Succeeded)
            {
                message = $"{message}\n{_localizer.Format("Commits.FetchFailed", fetch.Output)}";
            }
        }

        await PatchOperations.RefreshQuietlyAsync(() => delivered ? _tracker.MarkSentAsync(CancellationToken.None) : _tracker.RefreshAsync(CancellationToken.None));
        return new SendPatchResult(true, envelope.Id, result, message);
    }

    /// <summary>
    /// Takes the unsent commits from the oldest one up to the given one.
    /// </summary>
    /// <param name="unsent">The unsent commits, oldest first.</param>
    /// <param name="upTo">The last commit to take, or <see langword="null"/> for all of them.</param>
    /// <returns>The block.</returns>
    /// <exception cref="SyncBridgeException">The commit is not an unsent one.</exception>
    private IReadOnlyList<CommitInfo> SelectBlock(IReadOnlyList<CommitInfo> unsent, string? upTo)
    {
        if (string.IsNullOrWhiteSpace(upTo))
        {
            return unsent;
        }

        var wanted = upTo.Trim();
        var index = -1;
        for (var i = 0; i < unsent.Count; i++)
        {
            if (string.Equals(unsent[i].Hash, wanted, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return index >= 0
            ? unsent.Take(index + 1).ToArray()
            : throw new SyncBridgeException(_localizer.Format("Commits.NotUnsent", wanted));
    }

    /// <summary>
    /// Describes a send result for the user.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="delivery">The commits sent.</param>
    /// <returns>The message.</returns>
    private string Describe(PatchApplyResult result, CommitDelivery delivery) => result.Status switch
    {
        PatchStatus.Applied => _localizer.Format("Commits.AppliedOnPeer", delivery.Commits.Count, delivery.Branch),
        PatchStatus.Waiting => _localizer.Format("Commits.WaitingOnPeer", delivery.Commits.Count, LocalizedText.Render(result.Error, _localizer)),
        PatchStatus.Pending => result.Error ?? _localizer.Get("Patch.StatusUnknown"),
        _ => _localizer.Format("Patch.NotApplied", LocalizedText.Render(result.Error, _localizer))
    };

    /// <summary>
    /// Computes the SHA-256 hash of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash in lower-case hexadecimal digits.</returns>
    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

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
}
