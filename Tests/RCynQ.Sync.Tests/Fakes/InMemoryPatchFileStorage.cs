using System.Collections.Concurrent;
using NSubstitute;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Storage;
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
/// In-memory <see cref="IPatchFileStorage"/>.
/// </summary>
public sealed class InMemoryPatchFileStorage : IPatchFileStorage
{
    /// <summary>Gets the stored payloads.</summary>
    public ConcurrentDictionary<Guid, (byte[] Diff, byte[] Bundle)> Items { get; } = new();

    /// <inheritdoc />
    public Task SaveAsync(Guid patchId, byte[] diff, byte[] bundle, CancellationToken cancellationToken = default)
    {
        Items[patchId] = (diff, bundle);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.TryGetValue(patchId, out var item) ? item.Diff : throw new SyncBridgeException("missing"));

    /// <inheritdoc />
    public Task<byte[]> ReadBundleAsync(Guid patchId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.TryGetValue(patchId, out var item) ? item.Bundle : throw new SyncBridgeException("missing"));

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default) => Task.FromResult(Items.ContainsKey(patchId));
}
