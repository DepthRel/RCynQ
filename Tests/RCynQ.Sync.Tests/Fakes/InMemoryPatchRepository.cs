using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPatchRepository"/>.
/// </summary>
public sealed class InMemoryPatchRepository : IPatchRepository
{
    /// <summary>Gets the stored patches.</summary>
    public ConcurrentDictionary<Guid, PatchMetadata> Items { get; } = new();

    /// <inheritdoc />
    public Task<PatchMetadata> UpsertAsync(PatchMetadata patch, CancellationToken cancellationToken = default)
    {
        Items[patch.Id] = patch;
        return Task.FromResult(patch);
    }

    /// <inheritdoc />
    public Task<PatchMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.TryGetValue(id, out var patch) ? patch : null);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.ContainsKey(id));

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        => GetPageAsync(0, count, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetPageAsync(int skip, int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PatchMetadata>>(Items.Values
            .OrderByDescending(patch => patch.CreatedAtUtc)
            .ThenByDescending(patch => patch.Id)
            .Skip(skip)
            .Take(count)
            .ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetAllAsync(CancellationToken cancellationToken = default) => GetRecentAsync(int.MaxValue, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAsync(PatchDirection direction, PatchStatus status, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Values.Count(patch => patch.Direction == direction && patch.Status == status));

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetByStatusAsync(PatchDirection direction, PatchStatus status, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PatchMetadata>>(Items.Values
            .Where(patch => patch.Direction == direction && patch.Status == status)
            .OrderByDescending(patch => patch.CreatedAtUtc)
            .ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> DeleteOlderThanAsync(DateTimeOffset createdBeforeUtc, IReadOnlyCollection<PatchStatus> keepStatuses, CancellationToken cancellationToken = default)
    {
        var ids = Items.Values.Where(patch => patch.CreatedAtUtc < createdBeforeUtc && !keepStatuses.Contains(patch.Status)).Select(patch => patch.Id).ToArray();
        foreach (var id in ids)
        {
            Items.TryRemove(id, out _);
        }

        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }
}
