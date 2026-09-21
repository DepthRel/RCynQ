using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Storage;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISecretStore"/>.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    /// <summary>The stored values.</summary>
    private readonly ConcurrentDictionary<string, string> _values = new();

    /// <inheritdoc />
    public Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        _values[name] = value;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(_values.TryGetValue(name, out var value) ? value : null);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(_values.TryRemove(name, out _));

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<string>>(_values.Keys.ToArray());
}
