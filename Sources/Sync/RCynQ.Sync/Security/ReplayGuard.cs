using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

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
    /// Ensures that the envelope is fresh and was not processed before.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the envelope is accepted.</returns>
    /// <exception cref="PatchRejectedException">The envelope is stale, from the future or a replay.</exception>
    public async Task EnsureFreshAsync(PatchEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var now = _timeProvider.GetUtcNow();
        if (envelope.CreatedAtUtc > now + _options.MaxClockSkew)
        {
            throw new PatchRejectedException(PatchRejectionReason.Expired, $"Patch {envelope.Id} was created in the future ({envelope.CreatedAtUtc:O}); check the clocks of both nodes.");
        }

        if (envelope.CreatedAtUtc < now - _options.MaxAge)
        {
            throw new PatchRejectedException(PatchRejectionReason.Expired, $"Patch {envelope.Id} is older than {_options.MaxAge}.");
        }

        if (await _patches.ExistsAsync(envelope.Id, cancellationToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.Replay, $"Patch {envelope.Id} was already processed.");
        }
    }
}
