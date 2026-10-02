using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Locking;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the node has a sync base: one built from Git passes as soon as it can be built; a manual snapshot must also
/// match the recorded state hash, since nothing rebuilds it when it is changed outside RCynQ.
/// </summary>
public sealed class SnapshotIntegrityCheck : DiagnosticCheckBase
{
    /// <summary>The manifest builder.</summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>The state hasher.</summary>
    private readonly IStateHasher _hasher;

    /// <summary>The state store.</summary>
    private readonly INodeStateStore _state;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotIntegrityCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="state">The state store.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    public SnapshotIntegrityCheck(ILocalizer localizer, IFileManifestBuilder manifest, IStateHasher hasher, INodeStateStore state, SyncBaseManager syncBase, WorkingTreeLock workingTreeLock)
        : base("SnapshotIntegrity", _patchRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _manifest = manifest;
        _hasher = hasher;
        _state = state;
        _syncBase = syncBase;
        _lock = workingTreeLock;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        Core.State.SyncBaseStatus status;
        try
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                status = await _syncBase.EnsureAsync(cancellationToken);
            }
        }
        catch (SyncBridgeException ex)
        {
            return Fail(_localizer.Describe(ex), _localizer.Get("Diag.Snapshot.MissingFix"));
        }

        switch (status.Mode)
        {
            case SyncBaseMode.Git:
                return Pass(_localizer.Format("Diag.Snapshot.Git", status.Commit?[..Math.Min(12, status.Commit.Length)] ?? string.Empty, status.Branch ?? "HEAD"));

            case SyncBaseMode.None:
                return Fail(status.Problem ?? _localizer.Get("Diag.Snapshot.Missing"), _localizer.Get("Diag.Snapshot.MissingFix"));
        }

        var syncBase = context.Paths.SyncBaseDirectory;
        var recorded = (await _state.GetAsync(cancellationToken)).SyncBaseHash;
        // Read with the rules of the working tree, exactly as the state tracker recorded it.
        var entries = context.Node.EffectiveWorkdir is { } workdir
            ? await _manifest.ScanAsync(syncBase, workdir, cancellationToken)
            : await _manifest.ScanAsync(syncBase, cancellationToken);
        var actual = _hasher.ComputeHash(entries);
        if (recorded is null)
        {
            return Fail(_localizer.Get("Diag.Snapshot.NoHash"), _localizer.Get("Diag.Snapshot.NoHashFix"));
        }

        return recorded == actual
            ? Pass(_localizer.Format("Diag.Snapshot.Ok", actual[..12]))
            : Fail(_localizer.Format("Diag.Snapshot.Mismatch", actual[..12], recorded[..Math.Min(12, recorded.Length)]),
                _localizer.Get("Diag.Snapshot.MismatchFix"));
    }
}
