using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.Sync.Security;

/// <summary>
/// Rejects replayed and stale envelopes.
/// </summary>
/// <remarks>
/// An envelope is accepted once: its identifier must be unknown to the patch repository, and its creation time must be
/// inside <c>[now - MaxAge, now + MaxClockSkew]</c>. Because stale envelopes are refused, the repository only needs to
/// retain patch identifiers for <see cref="ReplayGuardOptions.MaxAge"/>.
/// </remarks>
public sealed class ReplayGuard
{
    /// <summary>
    /// The patch repository.
    /// </summary>
    private readonly IPatchRepository _patches;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly ReplayGuardOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayGuard"/> class.
    /// </summary>
    /// <param name="patches">The patch repository.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="options">The options.</param>
    public ReplayGuard(IPatchRepository patches, TimeProvider timeProvider, ReplayGuardOptions options)
    {
        _patches = patches;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>
    /// Gets the maximum age of an accepted envelope.
    /// </summary>
    public TimeSpan MaxAge => _options.MaxAge;

    /// <summary>
    /// Ensures that the envelope is fresh and was not processed before.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the envelope is accepted.</returns>
    /// <exception cref="PatchRejectedException">The envelope is stale, from the future or a replay.</exception>
    public async Task EnsureFreshAsync(PatchEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureInWindow(envelope);
        if (await _patches.ExistsAsync(envelope.Id, cancellationToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.Replay, Texts.Format("Replay.Again", envelope.Id));
        }
    }

    /// <summary>
    /// Ensures that the creation time of the envelope is inside the accepted window, without looking at earlier patches.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <remarks>
    /// Used to resume an open session: the identifier is known by design then, but a session may not be kept alive past the age limit.
    /// </remarks>
    /// <exception cref="PatchRejectedException">The envelope is stale or from the future.</exception>
    public void EnsureInWindow(PatchEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var now = _timeProvider.GetUtcNow();
        if (envelope.CreatedAtUtc > now + _options.MaxClockSkew)
        {
            throw new PatchRejectedException(PatchRejectionReason.Expired, Texts.Format("Replay.Future", envelope.Id, envelope.CreatedAtUtc));
        }

        if (envelope.CreatedAtUtc < now - _options.MaxAge)
        {
            throw new PatchRejectedException(PatchRejectionReason.Expired, Texts.Format("Replay.Old", envelope.Id, _options.MaxAge));
        }
    }
}
