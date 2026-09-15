using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the sync base exists and matches the recorded state hash.
/// </summary>
public sealed class SnapshotIntegrityCheck : DiagnosticCheckBase
{
    /// <summary>The manifest builder.</summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>The state hasher.</summary>
    private readonly IStateHasher _hasher;

    /// <summary>The state store.</summary>
    private readonly INodeStateStore _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotIntegrityCheck"/> class.
    /// </summary>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="state">The state store.</param>
    public SnapshotIntegrityCheck(IFileManifestBuilder manifest, IStateHasher hasher, INodeStateStore state)
        : base("SnapshotIntegrity", PatchRoles)
    {
        _manifest = manifest;
        _hasher = hasher;
        _state = state;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var syncBase = context.Paths.SyncBaseDirectory;
        if (!Directory.Exists(syncBase))
        {
            return Fail("The sync base snapshot does not exist.", "Run 'patch baseline' on this node while Source and Dest working trees are identical.");
        }

        var recorded = (await _state.GetAsync(cancellationToken)).SyncBaseHash;
        var actual = _hasher.ComputeHash(await _manifest.ScanAsync(syncBase, cancellationToken));
        if (recorded is null)
        {
            return Fail("No sync base hash is recorded.", "Run 'status' once to record the state, or recreate the baseline.");
        }

        return recorded == actual
            ? Pass($"Sync base matches recorded hash {actual[..12]}.")
            : Fail($"Sync base hash {actual[..12]} does not match the recorded {recorded[..Math.Min(12, recorded.Length)]}; the snapshot was modified outside RCynQ.",
                "Recreate the baseline with 'patch baseline' after making both working trees identical.");
    }
}
