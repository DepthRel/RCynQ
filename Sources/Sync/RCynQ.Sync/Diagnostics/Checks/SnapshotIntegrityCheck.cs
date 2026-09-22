using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Diagnostics;
using RCynQ.Sync.GitChannel;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the sync base exists and matches the recorded state hash; passes without a snapshot for a Source node that
/// compares against the branch last pulled from Dest instead (see <see cref="IPendingChangesBaseResolver"/>).
/// </summary>
public sealed class SnapshotIntegrityCheck : DiagnosticCheckBase
{
    /// <summary>The manifest builder.</summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>The state hasher.</summary>
    private readonly IStateHasher _hasher;

    /// <summary>The state store.</summary>
    private readonly INodeStateStore _state;

    /// <summary>The resolver of the directory to compare the working tree against.</summary>
    private readonly IPendingChangesBaseResolver _baseResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotIntegrityCheck"/> class.
    /// </summary>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="state">The state store.</param>
    /// <param name="baseResolver">The resolver of the directory to compare the working tree against.</param>
    public SnapshotIntegrityCheck(IFileManifestBuilder manifest, IStateHasher hasher, INodeStateStore state, IPendingChangesBaseResolver baseResolver)
        : base("SnapshotIntegrity", PatchRoles)
    {
        _manifest = manifest;
        _hasher = hasher;
        _state = state;
        _baseResolver = baseResolver;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        await using var sendBase = await _baseResolver.ResolveAsync(cancellationToken);
        if (sendBase is { IsGitNative: true })
        {
            return Pass(Texts.Get("Diag.Snapshot.GitNative"));
        }

        var syncBase = context.Paths.SyncBaseDirectory;
        if (!Directory.Exists(syncBase))
        {
            return Fail(Texts.Get("Diag.Snapshot.Missing"), Texts.Get("Diag.Snapshot.MissingFix"));
        }

        var recorded = (await _state.GetAsync(cancellationToken)).SyncBaseHash;
        var actual = _hasher.ComputeHash(await _manifest.ScanAsync(syncBase, cancellationToken));
        if (recorded is null)
        {
            return Fail(Texts.Get("Diag.Snapshot.NoHash"), Texts.Get("Diag.Snapshot.NoHashFix"));
        }

        return recorded == actual
            ? Pass(Texts.Format("Diag.Snapshot.Ok", actual[..12]))
            : Fail(Texts.Format("Diag.Snapshot.Mismatch", actual[..12], recorded[..Math.Min(12, recorded.Length)]),
                Texts.Get("Diag.Snapshot.MismatchFix"));
    }
}
