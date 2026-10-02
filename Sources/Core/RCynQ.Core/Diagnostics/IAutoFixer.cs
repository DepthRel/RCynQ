using RCynQ.Core.Enums;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// Executes proposed fixes from an allow-list of safe actions after explicit user confirmation.
/// </summary>
public interface IAutoFixer
{
    /// <summary>
    /// Checks whether a fix kind may ever be executed automatically.
    /// </summary>
    /// <param name="kind">The fix kind.</param>
    /// <returns><see langword="true"/> for allowed kinds.</returns>
    bool IsAllowed(DiagnosticFixKind kind);

    /// <summary>
    /// Executes a fix.
    /// </summary>
    /// <param name="fix">The proposed fix.</param>
    /// <param name="confirmed">Whether the user explicitly confirmed this fix.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; refused fixes are reported, not thrown.</returns>
    Task<AutoFixResult> ApplyAsync(DiagnosticFix fix, bool confirmed, CancellationToken cancellationToken = default);
}
