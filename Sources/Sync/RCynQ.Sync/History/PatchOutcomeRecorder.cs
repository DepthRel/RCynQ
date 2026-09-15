using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Sync.History;

/// <summary>
/// Records the outcome of a patch step: metadata, history and event.
/// </summary>
public sealed class PatchOutcomeRecorder
{
    /// <summary>
    /// The patch repository.
    /// </summary>
    private readonly IPatchRepository _patches;

    /// <summary>
    /// The history repository.
    /// </summary>
    private readonly ISyncHistoryRepository _history;

    /// <summary>
    /// The event publisher.
    /// </summary>
    private readonly IEventPublisher _events;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchOutcomeRecorder"/> class.
    /// </summary>
    /// <param name="patches">The patch repository.</param>
    /// <param name="history">The history repository.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    public PatchOutcomeRecorder(IPatchRepository patches, ISyncHistoryRepository history, IEventPublisher events, TimeProvider timeProvider)
    {
        _patches = patches;
        _history = history;
        _events = events;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Persists the outcome and publishes a <see cref="PatchEvent"/>.
    /// </summary>
    /// <param name="metadata">The metadata before the step.</param>
    /// <param name="result">The step result.</param>
    /// <param name="details">Optional history details.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated metadata.</returns>
    public async Task<PatchMetadata> RecordAsync(PatchMetadata metadata, PatchApplyResult result, string? details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(result);
        var now = _timeProvider.GetUtcNow();
        var updated = metadata.WithResult(result, now);
        await _patches.UpsertAsync(updated, cancellationToken);
        await _history.AddAsync(
            new SyncHistoryEntry(Guid.NewGuid(), now, metadata.Direction, metadata.SourceNode, metadata.TargetNode, metadata.Id, result.Status, result.AppliedFileCount, result.Conflicts.Count, details ?? result.Error),
            cancellationToken);
        await _events.PublishPatchAsync(
            new PatchEvent(metadata.Id, metadata.Direction, result.Status, result.AppliedFileCount, result.Conflicts.Count, result.Error ?? details, now),
            cancellationToken);
        return updated;
    }
}
