using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.State;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// Runs the checks applicable to the local role.
/// </summary>
public interface IDiagnosticsRunner
{
    /// <summary>
    /// Gets the checks that apply to a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The checks in execution order.</returns>
    IReadOnlyList<IDiagnosticCheck> GetApplicableChecks(NodeRole role);

    /// <summary>
    /// Runs all applicable checks, persists the run and publishes a <see cref="DiagnosticsEvent"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>The aggregated results.</returns>
    Task<DiagnosticsEvent> RunAsync(CancellationToken cancellationToken = default);
}
