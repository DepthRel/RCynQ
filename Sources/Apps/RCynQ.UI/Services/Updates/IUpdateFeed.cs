namespace RCynQ.UI.Services.Updates;

/// <summary>
/// Reads the list of the latest releases the project page publishes.
/// </summary>
public interface IUpdateFeed
{
    /// <summary>
    /// Reads the list.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The content of <c>updates.json</c>.</returns>
    /// <exception cref="HttpRequestException">The page could not be reached or answered with an error.</exception>
    /// <exception cref="TimeoutException">The page did not answer in time.</exception>
    /// <exception cref="InvalidDataException">The answer is larger than any list of releases.</exception>
    Task<string> ReadAsync(CancellationToken cancellationToken);
}
