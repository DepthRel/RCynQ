using RCynQ.Core.Events;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists diagnostics runs.
/// </summary>
public interface IDiagnosticRunRepository
{
    /// <summary>
    /// Stores a run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the run is stored.</returns>
    Task AddAsync(DiagnosticsEvent run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent runs, newest first.
    /// </summary>
    /// <param name="count">The maximum number of runs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The runs.</returns>
    Task<IReadOnlyList<DiagnosticsEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}
