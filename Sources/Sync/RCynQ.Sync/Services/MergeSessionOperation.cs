using System.Globalization;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Localization;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Content;
using RCynQ.Sync.Enums;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Merging;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Services;

/// <summary>
/// Resolves a conflicting incoming patch in a merge tool: prepares the files of the merge, then applies what the user
/// merged, or gives the merge up.
/// </summary>
/// <remarks>
/// <para>
/// Preparing and applying run under the working tree lock; the user merges in between, without the lock, so the node
/// keeps working. The merge is valid for the local files it was prepared from only: when one of them changed meanwhile,
/// nothing is written and the merge starts again.
/// </para>
/// <para>
/// Files Git merges on its own are written with the rest; only the files whose changes collide go to the tool. The
/// results are read from the directory of the session only, never from a path a client names. Writing works as the
/// "use Git" policy does: the local versions are backed up, every file is written in one transaction, and the sync base
/// takes the incoming content.
/// </para>
/// </remarks>
public sealed class MergeSessionOperation
{
    /// <summary>The longest name of a merge tool kept in the history.</summary>
    public const int MAX_TOOL_NAME_LENGTH = 100;

    /// <summary>The name of the directory of a session where Git works out the merge.</summary>
    private const string WORK_DIRECTORY = "work";

    /// <summary>The middle part of the name of the copy of the local version.</summary>
    private const string LOCAL_SUFFIX = ".local";

    /// <summary>The middle part of the name of the copy of the common ancestor.</summary>
    private const string BASE_SUFFIX = ".base";

    /// <summary>The middle part of the name of the copy of the incoming version.</summary>
    private const string INCOMING_SUFFIX = ".incoming";

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>Works out the merge of each file.</summary>
    private readonly MergePlanner _planner;

    /// <summary>Writes the outcome into the working tree.</summary>
    private readonly MergeOutcomeWriter _writer;

    /// <summary>Writes the incoming content into the sync base.</summary>
    private readonly ContentOverwriteApplier _contentApplier;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The sessions.</summary>
    private readonly MergeSessionStore _sessions;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Tells which repository the node works with now, or <see langword="null"/> for the one it started with.</summary>
    private readonly IActiveRepository? _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeSessionOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="planner">Works out the merge of each file.</param>
    /// <param name="writer">Writes the outcome into the working tree.</param>
    /// <param name="contentApplier">Writes the incoming content into the sync base.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="sessions">The sessions.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one the node started with.</param>
    public MergeSessionOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IPathUtility pathUtility,
        MergePlanner planner,
        MergeOutcomeWriter writer,
        ContentOverwriteApplier contentApplier,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        SyncBaseManager syncBase,
        MergeSessionStore sessions,
        TimeProvider timeProvider,
        IActiveRepository? repository = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _repository = repository;
        _node = node;
        _paths = paths;
        _pathUtility = pathUtility;
        _planner = planner;
        _writer = writer;
        _contentApplier = contentApplier;
        _patches = patches;
        _storage = storage;
        _outcomes = outcomes;
        _tracker = tracker;
        _lock = workingTreeLock;
        _syncBase = syncBase;
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Prepares the files of a conflicting patch for a merge tool.
    /// </summary>
    /// <param name="patchId">The patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The session with the files to merge; without a session, and with the files no tool can merge, when there are such files.
    /// </returns>
    /// <exception cref="SyncBridgeException">The node is outside the patch channel, or its sync base is not ready.</exception>
    /// <exception cref="KeyNotFoundException">The patch is unknown.</exception>
    /// <exception cref="InvalidOperationException">The patch is not an incoming patch that waits for a decision.</exception>
    public async Task<MergeSessionResponse> PrepareAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var metadata = await GetResolvableAsync(patchId, cancellationToken);
            await EnsureSyncBaseAsync(cancellationToken);

            var envelope = EnvelopeOf(metadata);
            var content = new StoredPatchContent(patchId, _storage);
            var sessionId = Guid.NewGuid();
            var directory = _sessions.CreateDirectory(sessionId);
            var scratch = Path.Combine(directory, WORK_DIRECTORY);
            Directory.CreateDirectory(scratch);
            try
            {
                var entries = new List<MergeSessionEntry>();
                var files = new List<MergeSessionFile>();
                var unmergeable = new List<ConflictFile>();
                var autoMerged = 0;
                for (var index = 0; index < envelope.Manifest.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var change = envelope.Manifest[index];
                    var plan = await _planner.PlanAsync(index, change, content, _paths.SyncBase, _paths.Workdir, scratch, cancellationToken);
                    switch (plan.Kind)
                    {
                        case MergePlanKind.CannotMerge:
                            unmergeable.Add(new ConflictFile(change.Path, null, change.Sha256, plan.Reason));
                            break;
                        case MergePlanKind.MergedWithMarkers:
                            var file = PlaceForTool(directory, index, change.Path, plan);
                            files.Add(file);
                            entries.Add(new MergeSessionEntry(change, plan.Kind, plan.LocalHash, file.ResultFile));
                            break;
                        case MergePlanKind.Merged:
                            autoMerged++;
                            entries.Add(new MergeSessionEntry(change, plan.Kind, plan.LocalHash, plan.MergedFile));
                            break;
                        default:
                            entries.Add(new MergeSessionEntry(change, plan.Kind, plan.LocalHash, null));
                            break;
                    }
                }

                if (unmergeable.Count > 0)
                {
                    DeleteQuietly(directory);
                    return new MergeSessionResponse(null, patchId, autoMerged, Array.Empty<MergeSessionFile>(), unmergeable);
                }

                _sessions.Add(new MergeSession(sessionId, patchId, directory, _timeProvider.GetUtcNow(), entries, files, autoMerged));
                return new MergeSessionResponse(sessionId, patchId, autoMerged, files, Array.Empty<ConflictFile>());
            }
            catch
            {
                DeleteQuietly(directory);
                throw;
            }
        }
    }

    /// <summary>
    /// Applies what the user merged in the tool.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="toolName">The name of the tool, recorded in the history.</param>
    /// <param name="cancellationToken">A token to cancel the operation before anything is written.</param>
    /// <returns>The resolution result.</returns>
    /// <exception cref="ArgumentException">The name of the tool is empty, too long or holds control characters.</exception>
    /// <exception cref="KeyNotFoundException">The session is unknown or expired.</exception>
    /// <exception cref="InvalidOperationException">
    /// The patch no longer waits for a decision, or a local file changed since the session was prepared; the session ends.
    /// </exception>
    /// <exception cref="SyncBridgeException">A merged file is missing, or the sync base is not ready; the session stays.</exception>
    /// <exception cref="PatchApplyException">The files could not be written; the working tree is as it was and the session stays.</exception>
    public async Task<PatchApplyResult> CompleteAsync(Guid sessionId, string toolName, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        var tool = ValidateToolName(toolName);

        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var session = _sessions.Get(sessionId) ?? throw new KeyNotFoundException(_localizer.Format("MergeSession.NotFound", sessionId));
            PatchMetadata metadata;
            try
            {
                metadata = await GetResolvableAsync(session.PatchId, cancellationToken);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                // The patch was decided some other way or removed: the merge has nothing to apply to any more.
                _sessions.Remove(sessionId);
                throw;
            }

            await EnsureSyncBaseAsync(cancellationToken);
            await EnsureLocalUnchangedAsync(session, cancellationToken);

            var envelope = EnvelopeOf(metadata);
            var content = new StoredPatchContent(metadata.Id, _storage);
            var (manifest, merged) = await CollectAsync(session, cancellationToken);

            // A failed write leaves the working tree as it was; the session stays, so the user can try again without merging again.
            await _writer.WriteAsync(envelope, content, manifest, merged, _paths.Workdir, cancellationToken);

            // From here the working tree is written. The rest is finished and recorded whatever the caller does meanwhile.
            try
            {
                // The incoming content is merged into the working tree, so it becomes the base, as with the "use Git" policy.
                await _contentApplier.ApplyAsync(envelope, content, _paths.SyncBase, CancellationToken.None);
                await _syncBase.RecordExchangeAsync(envelope.Manifest.Select(change => change.Path), CancellationToken.None);
                result = new PatchApplyResult(metadata.Id, PatchStatus.ConflictResolved, envelope.Manifest.Count, Array.Empty<ConflictFile>(), null);
                await _storage.DeleteContentAsync(metadata.Id, CancellationToken.None);
            }
            catch (Exception ex) when (ex is PatchApplyException or SyncBridgeException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(metadata.Id, _localizer.Describe(ex));
            }

            _sessions.Remove(sessionId);
            await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Patch.ResolvedInTool", tool), CancellationToken.None);
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(cancellationToken));
        return result;
    }

    /// <summary>
    /// Gives a merge up: the session and its files are removed, the patch keeps waiting for a decision.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <returns><see langword="true"/> when the session existed.</returns>
    public bool Cancel(Guid sessionId) => _sessions.Remove(sessionId);

    /// <summary>
    /// Reads a patch that can be resolved: an incoming one that waits for a decision or failed.
    /// </summary>
    /// <param name="patchId">The patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The metadata of the patch.</returns>
    /// <exception cref="KeyNotFoundException">The patch is unknown.</exception>
    /// <exception cref="InvalidOperationException">The patch cannot be resolved.</exception>
    private async Task<PatchMetadata> GetResolvableAsync(Guid patchId, CancellationToken cancellationToken)
    {
        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new KeyNotFoundException(_localizer.Format("Patch.NotFound", patchId));
        if (metadata.Direction != PatchDirection.Incoming || metadata.Status is not (PatchStatus.ConflictPending or PatchStatus.Failed))
        {
            throw new InvalidOperationException(_localizer.Format("Patch.NotResolvable", patchId, metadata.Direction, metadata.Status));
        }

        // A patch is resolved in the working tree it was sent for.
        if (!PatchRepositoryMembership.Belongs(_repository, metadata))
        {
            throw new InvalidOperationException(LocalizedError.Message("Repository.PatchOfAnother", patchId, metadata.Repository ?? string.Empty));
        }

        return metadata;
    }

    /// <summary>
    /// Makes sure the sync base, the common ancestor of the merge, is ready.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the base is ready.</returns>
    /// <exception cref="SyncBridgeException">The base is not ready.</exception>
    private async Task EnsureSyncBaseAsync(CancellationToken cancellationToken)
    {
        var syncBase = await _syncBase.EnsureAsync(cancellationToken);
        if (!syncBase.IsReady)
        {
            throw new SyncBridgeException(syncBase.Problem ?? _localizer.Get("Patch.NoBaseline"));
        }
    }

    /// <summary>
    /// Makes sure every local file is the one the merge was prepared from; otherwise the session ends.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the files are checked.</returns>
    /// <exception cref="InvalidOperationException">A local file changed.</exception>
    private async Task EnsureLocalUnchangedAsync(MergeSession session, CancellationToken cancellationToken)
    {
        foreach (var entry in session.Entries)
        {
            var local = _pathUtility.ResolveWithinRoot(_paths.Workdir, entry.Change.Path);
            if (await FileState.ContentHashAsync(local, cancellationToken) != entry.LocalHash)
            {
                _sessions.Remove(session.Id);
                throw new InvalidOperationException(_localizer.Format("MergeSession.LocalChanged", entry.Change.Path));
            }
        }
    }

    /// <summary>
    /// Collects the changes to write: incoming ones as they are, merged ones with the hash of the merged file.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The changes and the merged files by the path of their change.</returns>
    /// <exception cref="SyncBridgeException">A merged file is missing.</exception>
    private async Task<(List<FileChange> Manifest, Dictionary<string, string> Merged)> CollectAsync(MergeSession session, CancellationToken cancellationToken)
    {
        var manifest = new List<FileChange>();
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in session.Entries)
        {
            switch (entry.Kind)
            {
                case MergePlanKind.TakeIncoming:
                    manifest.Add(entry.Change);
                    break;
                case MergePlanKind.Merged:
                case MergePlanKind.MergedWithMarkers:
                    var file = entry.MergedFile!;
                    if (!File.Exists(file))
                    {
                        throw new SyncBridgeException(_localizer.Format("MergeSession.ResultMissing", entry.Change.Path));
                    }

                    manifest.Add(new FileChange(ChangeKind.Modified, entry.Change.Path, await DefaultStateHasher.Sha256FileAsync(file, cancellationToken), new FileInfo(file).Length, false));
                    merged[entry.Change.Path] = file;
                    break;
            }
        }

        return (manifest, merged);
    }

    /// <summary>
    /// Moves the versions of a file the merge tool gets into a folder of their own, named so the tool shows the kind of
    /// file and colors its syntax: <c>Foo.local.cs</c>, <c>Foo.base.cs</c>, <c>Foo.incoming.cs</c> and the result <c>Foo.cs</c>.
    /// </summary>
    /// <param name="directory">The directory of the session.</param>
    /// <param name="index">The position of the change in the patch.</param>
    /// <param name="path">The path of the file in the repository.</param>
    /// <param name="plan">The plan of the merge of the file.</param>
    /// <returns>The file of the session.</returns>
    private static MergeSessionFile PlaceForTool(string directory, int index, string path, MergePlan plan)
    {
        var folder = Path.Combine(directory, index.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        var local = Path.Combine(folder, stem + LOCAL_SUFFIX + extension);
        var @base = Path.Combine(folder, stem + BASE_SUFFIX + extension);
        var incoming = Path.Combine(folder, stem + INCOMING_SUFFIX + extension);
        var result = Path.Combine(folder, name);
        File.Move(plan.LocalFile!, local);
        File.Move(plan.BaseFile!, @base);
        File.Move(plan.IncomingFile!, incoming);
        File.Move(plan.MergedFile!, result);
        return new MergeSessionFile(path, local, @base, incoming, result);
    }

    /// <summary>
    /// Checks the name of a merge tool before it goes into the history.
    /// </summary>
    /// <param name="toolName">The name.</param>
    /// <returns>The trimmed name.</returns>
    /// <exception cref="ArgumentException">The name is empty, too long or holds control characters.</exception>
    private string ValidateToolName(string? toolName)
    {
        var name = toolName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MAX_TOOL_NAME_LENGTH || name.Any(char.IsControl))
        {
            throw new ArgumentException(_localizer.Format("MergeSession.BadToolName", MAX_TOOL_NAME_LENGTH), nameof(toolName));
        }

        return name;
    }

    /// <summary>
    /// Builds the envelope of a stored patch.
    /// </summary>
    /// <param name="metadata">The metadata of the patch.</param>
    /// <returns>The envelope.</returns>
    private static PatchEnvelope EnvelopeOf(PatchMetadata metadata) => new(
        metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash,
        metadata.Manifest, metadata.Signature, metadata.Repository);

    /// <summary>
    /// Removes the directory of a session that was not kept.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder belongs to no session; it is removed before the next session is prepared.
        }
    }
}
