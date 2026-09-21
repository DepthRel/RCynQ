using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Events;

/// <summary>
/// Event published when a diagnostics run completes.
/// </summary>
/// <param name="Role">The role the checks were selected for.</param>
/// <param name="Results">The check results.</param>
/// <param name="TimestampUtc">The event time.</param>
public sealed record DiagnosticsEvent(NodeRole Role, IReadOnlyList<DiagnosticResult> Results, DateTimeOffset TimestampUtc)
{
    /// <summary>
    /// Gets a value indicating whether every check passed.
    /// </summary>
    public bool AllPassed => Results.All(result => result.Passed);
}
