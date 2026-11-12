namespace RCynQ.UI.Services.Updates;

/// <summary>
/// Finds out whether a newer version of the application is published.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Gets the version that runs.
    /// </summary>
    Version Current { get; }

    /// <summary>
    /// Compares the running version with the latest published one.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>What the check found; a failure is a result, not an exception.</returns>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
