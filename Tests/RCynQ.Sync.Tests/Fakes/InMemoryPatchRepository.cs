using System.Collections.Concurrent;
using NSubstitute;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Patches;
using RCynQ.Git.Paths;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Security;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Sync.Transfer;

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
