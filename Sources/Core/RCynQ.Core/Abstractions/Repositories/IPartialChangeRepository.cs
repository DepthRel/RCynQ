using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Keeps the choices of a part of the changes of files: the versions that go to the other node instead of the working files.
/// </summary>
public interface IPartialChangeRepository
{
    /// <summary>
    /// Lists the choices of one repository.
    /// </summary>
    /// <param name="repositoryId">The local identifier of the repository.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The choices, by path (ordinal).</returns>
    Task<IReadOnlyList<PartialChange>> GetAllAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the choice of one file.
    /// </summary>
    /// <param name="repositoryId">The local identifier of the repository.</param>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The choice, or <see langword="null"/> when the whole file goes.</returns>
    Task<PartialChange?> GetAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the choice of a file, replacing the one it had.
    /// </summary>
    /// <param name="change">The choice.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the choice is stored.</returns>
    Task SaveAsync(PartialChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the choice of a file, so the whole file goes again.
    /// </summary>
    /// <param name="repositoryId">The local identifier of the repository.</param>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when there was a choice.</returns>
    Task<bool> RemoveAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default);
}
