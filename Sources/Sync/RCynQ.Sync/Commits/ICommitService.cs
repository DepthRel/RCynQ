using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Application facade of the delivery of commits used by the service endpoints, the CLI and the UI.
/// </summary>
public interface ICommitService
{
    /// <summary>
    /// Lists the commits of the current branch the Origin does not have yet (Workstation).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commits, oldest first, or the reason there are none to send.</returns>
    Task<UnsentCommitsResponse> GetUnsentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the unsent commits from the oldest one up to the given one (Workstation).
    /// </summary>
    /// <param name="upTo">The last commit to send, or <see langword="null"/> for every unsent commit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; <see cref="SendPatchResult.Sent"/> is <see langword="false"/> when there is nothing to send.</returns>
    /// <exception cref="SyncBridgeException">The commits cannot be sent.</exception>
    Task<SendPatchResult> SendAsync(string? upTo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back unsent commits (Workstation).
    /// </summary>
    /// <param name="target">The commit that becomes <c>HEAD</c>.</param>
    /// <param name="mode">What happens to the changes of the dropped commits.</param>
    /// <param name="expectedHead">The commit checked out when the user chose.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; a refusal is reported in the result.</returns>
    Task<GitOperationResult> ResetAsync(string target, CommitResetMode mode, string expectedHead, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a delivery of commits that waits, checking everything again (Origin).
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Applied, or still waiting with the reason.</returns>
    /// <exception cref="KeyNotFoundException">The delivery does not exist.</exception>
    /// <exception cref="InvalidOperationException">The delivery does not wait, or belongs to another repository.</exception>
    Task<PatchApplyResult> ApplyWaitingAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declines a delivery of commits that waits, dropping its bundle (Origin).
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    /// <exception cref="KeyNotFoundException">The delivery does not exist.</exception>
    /// <exception cref="InvalidOperationException">The delivery does not wait.</exception>
    Task<PatchApplyResult> DeclineWaitingAsync(Guid patchId, CancellationToken cancellationToken = default);
}
