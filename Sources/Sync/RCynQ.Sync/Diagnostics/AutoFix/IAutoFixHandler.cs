using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Executes one kind of safe automatic fix.
/// </summary>
public interface IAutoFixHandler
{
    /// <summary>
    /// Gets the handled fix kind.
    /// </summary>
    DiagnosticFixKind Kind { get; }

    /// <summary>
    /// Executes the fix.
    /// </summary>
    /// <param name="fix">The fix.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken);
}
