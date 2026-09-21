using RCynQ.Core.Enums;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// A single self-check of the node environment.
/// </summary>
public interface IDiagnosticCheck
{
    /// <summary>
    /// Gets the stable check name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the roles the check applies to.
    /// </summary>
    IReadOnlySet<NodeRole> Roles { get; }

    /// <summary>
    /// Runs the check. Implementations report problems as failed results instead of throwing.
    /// </summary>
    /// <param name="context">The diagnostic context.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The result.</returns>
    Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default);
}
