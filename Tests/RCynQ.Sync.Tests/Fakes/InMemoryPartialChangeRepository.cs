using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// Keeps the choices of a part of the changes of files in memory, one per repository and path.
/// </summary>
public sealed class InMemoryPartialChangeRepository : IPartialChangeRepository
{
    /// <summary>The choices by repository and path.</summary>
    private readonly Dictionary<(Guid Repository, string Path), PartialChange> _changes = new();

    /// <summary>Gets the stored choices.</summary>
    public IReadOnlyCollection<PartialChange> Items
    {
        get
        {
            lock (_changes)
            {
                return _changes.Values.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartialChange>> GetAllAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        lock (_changes)
        {
            return Task.FromResult<IReadOnlyList<PartialChange>>(_changes.Values.Where(change => change.RepositoryId == repositoryId).OrderBy(change => change.Path, StringComparer.Ordinal).ToArray());
        }
    }

    /// <inheritdoc />
    public Task<PartialChange?> GetAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default)
    {
        lock (_changes)
        {
            return Task.FromResult(_changes.GetValueOrDefault((repositoryId, path)));
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(PartialChange change, CancellationToken cancellationToken = default)
    {
        lock (_changes)
        {
            _changes[(change.RepositoryId, change.Path)] = change;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default)
    {
        lock (_changes)
        {
            return Task.FromResult(_changes.Remove((repositoryId, path)));
        }
    }
}
