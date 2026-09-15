namespace RCynQ.Core.Abstractions.Storage;

/// <summary>
/// Stores sensitive values (tokens, keys) with encryption at rest.
/// </summary>
/// <remarks>
/// Values are keyed by a logical name, such as the chain link token or the
/// peer public key reference.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Stores the given value under the given name, overwriting any previous value.
    /// </summary>
    /// <param name="name">The logical name of the secret. Must not be blank.</param>
    /// <param name="value">The secret value. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the secret has been stored.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the value stored under the given name.
    /// </summary>
    /// <param name="name">The logical name of the secret. Must not be blank.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the secret value, or <see langword="null"/> when
    /// no value is stored under the name.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the value stored under the given name.
    /// </summary>
    /// <param name="name">The logical name of the secret. Must not be blank.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when a value was deleted;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the names of all stored secrets.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the collection of secret names.</returns>
    Task<IReadOnlyCollection<string>> ListAsync(CancellationToken cancellationToken = default);
}
