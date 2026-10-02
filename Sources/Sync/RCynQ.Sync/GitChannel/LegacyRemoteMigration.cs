using System.Collections.Concurrent;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Repository;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Renames the remote-tracking namespace an earlier version used in the working copy of a Workstation.
/// </summary>
/// <remarks>
/// <para>
/// Earlier versions kept the branches received from the other node under <see cref="LEGACY_REMOTE_NAME"/>. The current
/// version keeps them under <see cref="GitChannelService.REMOTE_NAME"/>. Without the rename an updated node would see no
/// received branch and report that nothing was pulled yet.
/// </para>
/// <para>
/// Only a Workstation is touched: the repository of an Origin is never written to. A working copy is looked at once per
/// process.
/// </para>
/// </remarks>
public sealed class LegacyRemoteMigration
{
    /// <summary>
    /// The remote-tracking namespace of earlier versions.
    /// </summary>
    public const string LEGACY_REMOTE_NAME = "dest";

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The working copies already looked at.</summary>
    private readonly ConcurrentDictionary<string, bool> _checked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyRemoteMigration"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="git">The repository service.</param>
    public LegacyRemoteMigration(NodeConfig node, IGitRepositoryService git)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    /// <summary>
    /// Renames the namespace of an earlier version in the working copy, when it is there.
    /// </summary>
    /// <param name="workdir">The working copy.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the working copy was looked at.</returns>
    public async Task EnsureAsync(string workdir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workdir);
        if (_node.Role != NodeRole.Workstation || _checked.ContainsKey(workdir))
        {
            return;
        }

        try
        {
            if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
            {
                // Not remembered: the folder becomes a repository with the first Pull.
                return;
            }

            await _git.RenameRemoteAsync(workdir, LEGACY_REMOTE_NAME, GitChannelService.REMOTE_NAME, GitChannelService.REMOTE_PLACEHOLDER_URL, cancellationToken);
            _checked[workdir] = true;
        }
        catch (GitExecutionException)
        {
            // The rename is tried again on the next look; the caller then reports the missing branch as it always did.
        }
    }
}
