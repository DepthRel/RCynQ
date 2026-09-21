using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Repositories;
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
        => Task.FromResult<IReadOnlyList<PatchMetadata>>(Items.Values.OrderByDescending(patch => patch.CreatedAtUtc).Take(count).ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetAllAsync(CancellationToken cancellationToken = default) => GetRecentAsync(int.MaxValue, cancellationToken);
}
